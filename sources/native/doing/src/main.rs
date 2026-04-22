use anyhow::{Context, Result, bail};
use globset::{Glob, GlobSet, GlobSetBuilder};
use ignore::{WalkBuilder, WalkState};
use owo_colors::OwoColorize;
use serde::{Deserialize, Serialize};
use std::collections::{BTreeMap, BTreeSet};
use std::env;
use std::ffi::{OsStr, OsString};
use std::fs;
use std::io::{self, IsTerminal};
use std::path::{Path, PathBuf};
use std::process::{self, Command};
use std::sync::{Arc, Mutex};
use time::{OffsetDateTime, format_description::well_known::Rfc3339};
use xxhash_rust::xxh3::xxh3_128;

const CONFIG_VERSION: u32 = 1;
const CACHE_VERSION: u32 = 1;
const DEFAULT_SCAN_ROOTS: &[&str] = &["."];
const DEFAULT_EXCLUDE_GLOBS: &[&str] = &["**/bin/**", "**/obj/**", "**/.git/**", "**/.doing/**"];
const ROOT_LEVEL_INPUTS: &[&str] = &[
    "global.json",
    ".editorconfig",
    ".globalconfig",
    "nuget.config",
    "Directory.Build.props",
    "Directory.Build.targets",
    "Directory.Build.rsp",
    "Directory.Packages.props",
    "Directory.Packages.targets",
];
const LOGO_DISABLE_ENV_VARS: &[&str] = &["GITHUB_ACTION", "CI", "DOING_NOLOGO", "NOGOLO"];
const BANNER_LINES: &[&str] = &[
    "  ____        _             ",
    " |  _ \\\\  ___ (_)_ __   __ _ ",
    " | | | |/ _ \\\\| | '_ \\\\ / _` |",
    " | |_| | (_) | | | | | (_| |",
    " |____/ \\\\___/|_|_| |_|\\\\__, |",
    "                       |___/ ",
];

fn main() {
    let exit_code = match program_main() {
        Ok(code) => code,
        Err(error) => {
            eprintln!(
                "{} {error:#}",
                Ui::new().tag_stderr("󰅙", "error", (255, 105, 97))
            );
            1
        }
    };

    process::exit(exit_code);
}

fn program_main() -> Result<i32> {
    let original_dir = env::current_dir().context("failed to get current working directory")?;
    let forwarded_args = env::args_os().skip(1).collect::<Vec<_>>();

    run_launcher(original_dir, forwarded_args, OsStr::new("dotnet"))
}

fn run_launcher(
    original_dir: PathBuf,
    forwarded_args: Vec<OsString>,
    dotnet_program: &OsStr,
) -> Result<i32> {
    let ui = Ui::new();
    let doing_dir = find_doing_dir_from(&original_dir).ok_or_else(|| {
        anyhow::anyhow!(
            "failed to locate `.doing` from `{}` or any of its parent directories",
            original_dir.display()
        )
    })?;
    let config = Config::load(&doing_dir)?;

    report_gitignore_status(gitignore_status(&config.doing_dir), &ui);

    let cache_path = config.doing_dir.join("cache.toml");
    if should_print_banner(&cache_path) {
        print_banner(&config, &ui);
    }

    let current_snapshot = snapshot_current_inputs(&config)?;
    let cached_snapshot = load_cache(&cache_path, &ui);
    let rebuild_reason = rebuild_reason(
        cached_snapshot.as_ref(),
        &current_snapshot,
        &config.main_dll,
    );

    if let Some(reason) = rebuild_reason {
        ui.info(format!("󱐋 cache miss: {reason}"));
        let build_exit = run_build(&config, dotnet_program, &ui)?;
        if build_exit != 0 {
            return Ok(build_exit);
        }

        let refreshed_snapshot = snapshot_current_inputs(&config)?;
        if let Err(error) = write_cache(&cache_path, &refreshed_snapshot) {
            ui.warn(format!("failed to update cache.toml: {error:#}"));
        } else {
            ui.success("updated .doing/cache.toml");
        }
    } else {
        ui.success("cache hit, reusing the existing Release build");
    }

    run_exec(&config, dotnet_program, &forwarded_args, &original_dir, &ui)
}

#[derive(Debug, Deserialize)]
struct Config {
    version: u32,
    name: String,
    description: String,
    main_project: String,
    main_dll: String,
    #[serde(default)]
    solution: Option<String>,
    #[serde(default = "default_scan_roots")]
    scan_roots: Vec<String>,
    #[serde(default = "default_exclude_globs")]
    exclude_globs: Vec<String>,
    #[serde(default)]
    extra_inputs: Vec<String>,
}

#[derive(Debug, Clone)]
struct ResolvedConfig {
    root_dir: PathBuf,
    doing_dir: PathBuf,
    name: String,
    description: String,
    main_project: PathBuf,
    main_project_rel: String,
    main_dll: PathBuf,
    solution: Option<PathBuf>,
    scan_roots: Vec<PathBuf>,
    extra_inputs: Vec<PathBuf>,
    exclude_set: Arc<GlobSet>,
}

impl Config {
    fn load(doing_dir: &Path) -> Result<ResolvedConfig> {
        let config_path = doing_dir.join("config.toml");
        let contents = fs::read_to_string(&config_path)
            .with_context(|| format!("failed to read `{}`", config_path.display()))?;
        let config: Self = toml::from_str(&contents)
            .with_context(|| format!("failed to parse `{}`", config_path.display()))?;

        if config.version != CONFIG_VERSION {
            bail!(
                "unsupported config version `{}` in `{}`; expected `{CONFIG_VERSION}`",
                config.version,
                config_path.display()
            );
        }

        let root_dir = canonicalize_existing(
            doing_dir
                .parent()
                .ok_or_else(|| anyhow::anyhow!("`.doing` directory has no parent"))?,
        )?;
        let doing_dir = canonicalize_existing(doing_dir)?;
        let exclude_set = Arc::new(build_exclude_set(&config.exclude_globs)?);

        let main_project =
            canonicalize_existing(&resolve_from_root(&root_dir, &config.main_project))?;
        if !main_project.is_file() {
            bail!(
                "`main_project` must point to a file: `{}`",
                main_project.display()
            );
        }

        let main_project_rel = relative_path_string(&root_dir, &main_project)?;
        let main_dll = normalize_path(&resolve_from_root(&root_dir, &config.main_dll));

        let solution = match config.solution.as_deref() {
            Some(raw) => {
                let path = canonicalize_existing(&resolve_from_root(&root_dir, raw))?;
                if !path.is_file() {
                    bail!("`solution` must point to a file: `{}`", path.display());
                }
                Some(path)
            }
            None => None,
        };

        let mut scan_roots = Vec::new();
        for raw in &config.scan_roots {
            let path = canonicalize_existing(&resolve_from_root(&root_dir, raw))?;
            if !path.is_dir() {
                bail!("scan root must point to a directory: `{}`", path.display());
            }
            scan_roots.push(path);
        }

        let mut extra_inputs = Vec::new();
        for raw in &config.extra_inputs {
            let path = resolve_from_root(&root_dir, raw);
            if path.exists() {
                extra_inputs.push(canonicalize_existing(&path)?);
            }
        }

        Ok(ResolvedConfig {
            root_dir,
            doing_dir,
            name: config.name,
            description: config.description,
            main_project,
            main_project_rel,
            main_dll,
            solution,
            scan_roots,
            extra_inputs,
            exclude_set,
        })
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
struct TrackedInput {
    path: PathBuf,
    relative_path: String,
    kind: String,
    hash: String,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
struct CacheSnapshot {
    version: u32,
    built_at_utc: String,
    input_fingerprint: String,
    files: Vec<CacheFileRecord>,
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq, Eq)]
struct CacheFileRecord {
    path: String,
    kind: String,
    hash: String,
}

#[derive(Debug, Clone, Copy)]
enum WalkMode {
    SourceOnly,
    StructuralOnly,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum GitignoreStatus {
    MissingFile,
    MissingCacheEntry,
    Ok,
}

struct Ui {
    stdout_color: bool,
    stderr_color: bool,
}

impl Ui {
    fn new() -> Self {
        Self {
            stdout_color: io::stdout().is_terminal() && !no_color_requested(),
            stderr_color: io::stderr().is_terminal() && !no_color_requested(),
        }
    }

    fn tag_stdout(&self, icon: &str, label: &str, rgb: (u8, u8, u8)) -> String {
        style_tag(self.stdout_color, icon, label, rgb)
    }

    fn tag_stderr(&self, icon: &str, label: &str, rgb: (u8, u8, u8)) -> String {
        style_tag(self.stderr_color, icon, label, rgb)
    }

    fn info(&self, message: impl AsRef<str>) {
        println!(
            "{} {}",
            self.tag_stdout("󰋽", "info", (115, 193, 255)),
            message.as_ref()
        );
    }

    fn success(&self, message: impl AsRef<str>) {
        println!(
            "{} {}",
            self.tag_stdout("󰄬", "ok", (117, 255, 151)),
            message.as_ref()
        );
    }

    fn warn(&self, message: impl AsRef<str>) {
        eprintln!(
            "{} {}",
            self.tag_stderr("󰀪", "warn", (255, 189, 89)),
            message.as_ref()
        );
    }
}

fn default_scan_roots() -> Vec<String> {
    DEFAULT_SCAN_ROOTS
        .iter()
        .map(|value| (*value).to_owned())
        .collect()
}

fn default_exclude_globs() -> Vec<String> {
    DEFAULT_EXCLUDE_GLOBS
        .iter()
        .map(|value| (*value).to_owned())
        .collect()
}

fn find_doing_dir_from(start_dir: &Path) -> Option<PathBuf> {
    let mut current = start_dir.to_path_buf();

    loop {
        let candidate = current.join(".doing");
        if candidate.is_dir() {
            return Some(candidate);
        }

        if !current.pop() {
            return None;
        }
    }
}

fn should_print_banner(cache_path: &Path) -> bool {
    !cache_path.exists()
        && !LOGO_DISABLE_ENV_VARS.iter().any(|name| {
            env::var(name)
                .map(|value| is_truthy_env_value(&value))
                .unwrap_or(false)
        })
}

fn print_banner(config: &ResolvedConfig, ui: &Ui) {
    for (index, line) in BANNER_LINES.iter().enumerate() {
        let rgb = match index {
            0 => (255, 123, 172),
            1 => (255, 151, 98),
            2 => (255, 205, 86),
            3 => (98, 214, 156),
            4 => (86, 187, 255),
            _ => (174, 142, 255),
        };

        if ui.stdout_color {
            println!("{}", line.truecolor(rgb.0, rgb.1, rgb.2).bold());
        } else {
            println!("{line}");
        }
    }

    println!(
        "{} {}",
        ui.tag_stdout("󰎙", "project", (255, 151, 98)),
        config.name
    );
    println!(
        "{} {}",
        ui.tag_stdout("󰇮", "about", (98, 214, 156)),
        config.description
    );
    println!(
        "{} {}",
        ui.tag_stdout("󰌹", "repo", (86, 187, 255)),
        env!("CARGO_PKG_REPOSITORY")
    );
    println!(
        "{} {}",
        ui.tag_stdout("󰆍", "entry", (174, 142, 255)),
        config.main_project_rel
    );
    println!();
}

fn gitignore_status(doing_dir: &Path) -> GitignoreStatus {
    let gitignore_path = doing_dir.join(".gitignore");
    let contents = match fs::read_to_string(&gitignore_path) {
        Ok(contents) => contents,
        Err(_) => return GitignoreStatus::MissingFile,
    };

    if contents
        .lines()
        .any(|line| line.trim().trim_matches('/') == "cache.toml")
    {
        GitignoreStatus::Ok
    } else {
        GitignoreStatus::MissingCacheEntry
    }
}

fn report_gitignore_status(status: GitignoreStatus, ui: &Ui) {
    match status {
        GitignoreStatus::Ok => {}
        GitignoreStatus::MissingFile => ui.warn(
            "`.doing/.gitignore` is missing. Add `cache.toml` so the cache file stays out of version control.",
        ),
        GitignoreStatus::MissingCacheEntry => ui.warn(
            "`.doing/.gitignore` does not contain `cache.toml`. Add it to avoid committing local cache state.",
        ),
    }
}

fn snapshot_current_inputs(config: &ResolvedConfig) -> Result<CacheSnapshot> {
    let tracked_inputs = collect_tracked_inputs(config)?;
    Ok(to_cache_snapshot(&tracked_inputs))
}

fn collect_tracked_inputs(config: &ResolvedConfig) -> Result<Vec<TrackedInput>> {
    let collected = Arc::new(Mutex::new(BTreeSet::new()));

    for scan_root in &config.scan_roots {
        collect_with_walk(
            scan_root,
            &config.root_dir,
            Arc::clone(&config.exclude_set),
            WalkMode::SourceOnly,
            Arc::clone(&collected),
        )?;
    }

    collect_with_walk(
        &config.root_dir,
        &config.root_dir,
        Arc::clone(&config.exclude_set),
        WalkMode::StructuralOnly,
        Arc::clone(&collected),
    )?;

    {
        let mut guard = collected.lock().expect("input collection mutex poisoned");
        insert_explicit_inputs(&mut guard, config)?;
    }

    let mut tracked_inputs = Vec::new();
    let paths = {
        let guard = collected.lock().expect("input collection mutex poisoned");
        guard.iter().cloned().collect::<Vec<_>>()
    };

    for path in paths {
        if !path.is_file() {
            continue;
        }

        let relative_path = relative_path_string(&config.root_dir, &path)?;
        if config.exclude_set.is_match(&relative_path) {
            continue;
        }

        tracked_inputs.push(TrackedInput {
            kind: classify_input(&path),
            hash: file_hash_hex(&path)?,
            path,
            relative_path,
        });
    }

    tracked_inputs.sort_by(|left, right| left.relative_path.cmp(&right.relative_path));
    Ok(tracked_inputs)
}

fn insert_explicit_inputs(paths: &mut BTreeSet<PathBuf>, config: &ResolvedConfig) -> Result<()> {
    insert_if_file(paths, &config.main_project);
    if let Some(solution) = &config.solution {
        insert_if_file(paths, solution);
    }

    for raw in ROOT_LEVEL_INPUTS {
        let candidate = config.root_dir.join(raw);
        insert_if_file(paths, &candidate);
    }

    for extra in &config.extra_inputs {
        insert_if_file(paths, extra);
    }

    let mut current = config
        .main_project
        .parent()
        .ok_or_else(|| anyhow::anyhow!("main project has no parent directory"))?
        .to_path_buf();

    loop {
        if current.starts_with(&config.root_dir) {
            for entry in fs::read_dir(&current)
                .with_context(|| format!("failed to read directory `{}`", current.display()))?
            {
                let entry = entry.with_context(|| {
                    format!("failed to read an entry under `{}`", current.display())
                })?;
                let path = entry.path();
                if path.is_file() && is_directory_overlay_file(&path) {
                    insert_if_file(paths, &path);
                }
            }
        }

        if current == config.root_dir {
            break;
        }

        if !current.pop() {
            break;
        }
    }

    Ok(())
}

fn insert_if_file(paths: &mut BTreeSet<PathBuf>, path: &Path) {
    if path.is_file() {
        paths.insert(normalize_path(path));
    }
}

fn collect_with_walk(
    walk_root: &Path,
    root_dir: &Path,
    exclude_set: Arc<GlobSet>,
    mode: WalkMode,
    collected: Arc<Mutex<BTreeSet<PathBuf>>>,
) -> Result<()> {
    let mut builder = WalkBuilder::new(walk_root);
    WalkBuilder::hidden(&mut builder, false);
    builder.git_ignore(false);
    builder.git_exclude(false);
    builder.git_global(false);
    builder.parents(false);
    builder.follow_links(false);

    let root_dir = root_dir.to_path_buf();
    let walk_root = walk_root.to_path_buf();

    builder.build_parallel().run(|| {
        let exclude_set = Arc::clone(&exclude_set);
        let collected = Arc::clone(&collected);
        let root_dir = root_dir.clone();
        let walk_root = walk_root.clone();

        Box::new(move |entry| {
            let directory_entry = match entry {
                Ok(entry) => entry,
                Err(_) => return WalkState::Continue,
            };

            if !directory_entry
                .file_type()
                .map(|file_type| file_type.is_file())
                .unwrap_or(false)
            {
                return WalkState::Continue;
            }

            let path = normalize_path(directory_entry.path());
            let relative = match relative_path_string(&root_dir, &path) {
                Ok(relative) => relative,
                Err(_) => return WalkState::Continue,
            };

            if exclude_set.is_match(&relative) || !path.starts_with(&walk_root) {
                return WalkState::Continue;
            }

            if should_track_in_mode(&path, mode) {
                collected
                    .lock()
                    .expect("input collection mutex poisoned")
                    .insert(path);
            }

            WalkState::Continue
        })
    });

    Ok(())
}

fn should_track_in_mode(path: &Path, mode: WalkMode) -> bool {
    match mode {
        WalkMode::SourceOnly => path.extension().and_then(|value| value.to_str()) == Some("cs"),
        WalkMode::StructuralOnly => is_structural_input(path),
    }
}

fn is_structural_input(path: &Path) -> bool {
    let file_name = path.file_name().and_then(|value| value.to_str());
    let extension = path.extension().and_then(|value| value.to_str());

    matches!(
        extension,
        Some("csproj" | "props" | "targets" | "sln" | "slnx" | "ruleset")
    ) || matches!(
        file_name,
        Some(
            "packages.lock.json"
                | "global.json"
                | ".editorconfig"
                | ".globalconfig"
                | "nuget.config"
                | "Directory.Build.rsp"
        )
    )
}

fn is_directory_overlay_file(path: &Path) -> bool {
    let file_name = path.file_name().and_then(|value| value.to_str());
    matches!(
        file_name,
        Some(name) if name.starts_with("Directory.") && (name.ends_with(".props") || name.ends_with(".targets"))
    )
}

fn classify_input(path: &Path) -> String {
    let file_name = path
        .file_name()
        .and_then(|value| value.to_str())
        .unwrap_or_default();
    let extension = path
        .extension()
        .and_then(|value| value.to_str())
        .unwrap_or_default();

    match (file_name, extension) {
        (_, "cs") => "cs",
        (_, "csproj") => "project",
        (_, "sln" | "slnx") => "solution",
        ("packages.lock.json", _) => "lock",
        (_, "props") => "props",
        (_, "targets") => "targets",
        (
            "global.json"
            | ".editorconfig"
            | ".globalconfig"
            | "nuget.config"
            | "Directory.Build.rsp",
            _,
        ) => "config",
        (_, "ruleset") => "ruleset",
        _ => "input",
    }
    .to_owned()
}

fn load_cache(cache_path: &Path, ui: &Ui) -> Option<CacheSnapshot> {
    let contents = fs::read_to_string(cache_path).ok()?;
    match toml::from_str::<CacheSnapshot>(&contents) {
        Ok(snapshot) => Some(snapshot),
        Err(error) => {
            ui.warn(format!(
                "failed to parse `{}`. The cache will be ignored: {error}",
                cache_path.display()
            ));
            None
        }
    }
}

fn rebuild_reason(
    cached_snapshot: Option<&CacheSnapshot>,
    current_snapshot: &CacheSnapshot,
    main_dll: &Path,
) -> Option<String> {
    if !main_dll.is_file() {
        return Some(format!("`{}` does not exist yet", main_dll.display()));
    }

    let cached_snapshot = match cached_snapshot {
        Some(snapshot) => snapshot,
        None => return Some("cache.toml is missing or unreadable".to_owned()),
    };

    if cached_snapshot.version != CACHE_VERSION {
        return Some(format!(
            "cache schema version `{}` does not match `{CACHE_VERSION}`",
            cached_snapshot.version
        ));
    }

    if cached_snapshot.input_fingerprint != current_snapshot.input_fingerprint {
        return Some(diff_summary(cached_snapshot, current_snapshot));
    }

    if cached_snapshot.files != current_snapshot.files {
        return Some("tracked input metadata changed".to_owned());
    }

    None
}

fn diff_summary(cached_snapshot: &CacheSnapshot, current_snapshot: &CacheSnapshot) -> String {
    let old_files = cached_snapshot
        .files
        .iter()
        .map(|file| (file.path.as_str(), file))
        .collect::<BTreeMap<_, _>>();
    let new_files = current_snapshot
        .files
        .iter()
        .map(|file| (file.path.as_str(), file))
        .collect::<BTreeMap<_, _>>();

    for (path, new_file) in &new_files {
        match old_files.get(path) {
            None => return format!("new tracked input detected: {path}"),
            Some(old_file) if old_file.hash != new_file.hash => {
                return format!("tracked input changed: {path}");
            }
            Some(old_file) if old_file.kind != new_file.kind => {
                return format!("tracked input classification changed: {path}");
            }
            Some(_) => {}
        }
    }

    for path in old_files.keys() {
        if !new_files.contains_key(path) {
            return format!("tracked input disappeared: {path}");
        }
    }

    "tracked inputs changed".to_owned()
}

fn to_cache_snapshot(tracked_inputs: &[TrackedInput]) -> CacheSnapshot {
    let files = tracked_inputs
        .iter()
        .map(|input| CacheFileRecord {
            path: input.relative_path.clone(),
            kind: input.kind.clone(),
            hash: input.hash.clone(),
        })
        .collect::<Vec<_>>();

    CacheSnapshot {
        version: CACHE_VERSION,
        built_at_utc: OffsetDateTime::now_utc()
            .format(&Rfc3339)
            .expect("RFC3339 formatting must succeed"),
        input_fingerprint: aggregate_fingerprint(&files),
        files,
    }
}

fn aggregate_fingerprint(files: &[CacheFileRecord]) -> String {
    let mut bytes = Vec::new();
    for file in files {
        bytes.extend_from_slice(file.path.as_bytes());
        bytes.push(0);
        bytes.extend_from_slice(file.kind.as_bytes());
        bytes.push(0);
        bytes.extend_from_slice(file.hash.as_bytes());
        bytes.push(b'\n');
    }

    format!("{:032x}", xxh3_128(&bytes))
}

fn write_cache(cache_path: &Path, snapshot: &CacheSnapshot) -> Result<()> {
    let toml = toml::to_string_pretty(snapshot).context("failed to serialize cache.toml")?;
    let temp_path = cache_path.with_extension(format!("toml.tmp.{}", process::id()));
    fs::write(&temp_path, toml)
        .with_context(|| format!("failed to write `{}`", temp_path.display()))?;
    fs::rename(&temp_path, cache_path).with_context(|| {
        format!(
            "failed to atomically replace cache file `{}`",
            cache_path.display()
        )
    })?;
    Ok(())
}

fn run_build(config: &ResolvedConfig, dotnet_program: &OsStr, ui: &Ui) -> Result<i32> {
    ui.info(format!("building `{}` in Release", config.main_project_rel));

    let status = Command::new(dotnet_program)
        .arg("build")
        .arg(&config.main_project_rel)
        .arg("-c")
        .arg("Release")
        .current_dir(&config.root_dir)
        .status()
        .with_context(|| format!("failed to launch `{}`", dotnet_program.to_string_lossy()))?;

    Ok(exit_code_from_status(status))
}

fn run_exec(
    config: &ResolvedConfig,
    dotnet_program: &OsStr,
    forwarded_args: &[OsString],
    original_dir: &Path,
    ui: &Ui,
) -> Result<i32> {
    ui.info(format!(
        "launching `{}` with dotnet exec",
        config.main_dll.display()
    ));

    let mut command = Command::new(dotnet_program);
    command.arg("exec").arg(&config.main_dll);
    command.args(forwarded_args);
    command.current_dir(original_dir);

    let status = command
        .status()
        .with_context(|| format!("failed to launch `{}`", dotnet_program.to_string_lossy()))?;

    Ok(exit_code_from_status(status))
}

fn exit_code_from_status(status: process::ExitStatus) -> i32 {
    status.code().unwrap_or(1)
}

fn resolve_from_root(root_dir: &Path, raw: &str) -> PathBuf {
    let raw_path = Path::new(raw);
    if raw_path.is_absolute() {
        normalize_path(raw_path)
    } else {
        normalize_path(&root_dir.join(raw_path))
    }
}

fn canonicalize_existing(path: &Path) -> Result<PathBuf> {
    fs::canonicalize(path).with_context(|| format!("failed to resolve `{}`", path.display()))
}

fn relative_path_string(root_dir: &Path, path: &Path) -> Result<String> {
    let relative = path.strip_prefix(root_dir).with_context(|| {
        format!(
            "`{}` is not inside the tracked root `{}`",
            path.display(),
            root_dir.display()
        )
    })?;

    let value = relative
        .iter()
        .map(|part| part.to_string_lossy().replace('\\', "/"))
        .collect::<Vec<_>>()
        .join("/");

    Ok(if value.is_empty() {
        ".".to_owned()
    } else {
        value
    })
}

fn normalize_path(path: &Path) -> PathBuf {
    let mut normalized = PathBuf::new();
    for component in path.components() {
        match component {
            std::path::Component::CurDir => {}
            std::path::Component::ParentDir => {
                normalized.pop();
            }
            other => normalized.push(other.as_os_str()),
        }
    }

    if normalized.as_os_str().is_empty() {
        PathBuf::from(".")
    } else {
        normalized
    }
}

fn build_exclude_set(patterns: &[String]) -> Result<GlobSet> {
    let mut builder = GlobSetBuilder::new();
    for pattern in patterns {
        builder.add(
            Glob::new(pattern)
                .with_context(|| format!("invalid exclude glob pattern `{pattern}`"))?,
        );
    }

    builder.build().context("failed to build exclude glob set")
}

fn file_hash_hex(path: &Path) -> Result<String> {
    let bytes = fs::read(path).with_context(|| format!("failed to read `{}`", path.display()))?;
    Ok(format!("{:032x}", xxh3_128(&bytes)))
}

fn is_truthy_env_value(value: &str) -> bool {
    matches!(
        value.trim().to_ascii_lowercase().as_str(),
        "1" | "true" | "on" | "yes"
    )
}

fn no_color_requested() -> bool {
    env::var_os("NO_COLOR").is_some() || env::var_os("NOCOLOR").is_some()
}

fn style_tag(color_enabled: bool, icon: &str, label: &str, rgb: (u8, u8, u8)) -> String {
    if color_enabled {
        format!(
            "{} {}",
            icon.truecolor(rgb.0, rgb.1, rgb.2).bold(),
            label.truecolor(rgb.0, rgb.1, rgb.2).bold()
        )
    } else {
        format!("[{label}]")
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use anyhow::Result;
    use std::os::unix::fs::PermissionsExt;
    use tempfile::TempDir;

    #[test]
    fn finds_nearest_doing_directory_from_nested_path() -> Result<()> {
        let fixture = Fixture::new()?;
        let nested_dir = fixture.root.join("sources/managed/Doing.Cli");

        let found = find_doing_dir_from(&nested_dir).expect("should find .doing");

        assert_eq!(
            canonicalize_existing(&found)?,
            canonicalize_existing(&fixture.doing_dir)?
        );
        Ok(())
    }

    #[test]
    fn truthy_env_values_are_case_insensitive() {
        assert!(is_truthy_env_value("1"));
        assert!(is_truthy_env_value("TRUE"));
        assert!(is_truthy_env_value("On"));
        assert!(is_truthy_env_value(" yes "));
        assert!(!is_truthy_env_value(""));
        assert!(!is_truthy_env_value("0"));
        assert!(!is_truthy_env_value("false"));
    }

    #[test]
    fn collects_expected_inputs_and_excludes_build_artifacts() -> Result<()> {
        let fixture = Fixture::new()?;
        fs::write(
            fixture
                .root
                .join("sources/managed/Doing.Cli/obj/Debug/net10.0/Ignored.cs"),
            "class Ignored {}",
        )?;
        fs::write(
            fixture
                .root
                .join("sources/managed/Doing.Cli/bin/Release/generated.targets"),
            "<Project />",
        )?;

        let config = Config::load(&fixture.doing_dir)?;
        let tracked = collect_tracked_inputs(&config)?;
        let tracked_paths = tracked
            .iter()
            .map(|input| input.relative_path.as_str())
            .collect::<Vec<_>>();

        assert!(tracked_paths.contains(&"Directory.Build.props"));
        assert!(tracked_paths.contains(&"Directory.Packages.props"));
        assert!(tracked_paths.contains(&"Doing.slnx"));
        assert!(tracked_paths.contains(&"global.json"));
        assert!(tracked_paths.contains(&".editorconfig"));
        assert!(tracked_paths.contains(&"sources/managed/Doing.Cli/Doing.Cli.csproj"));
        assert!(tracked_paths.contains(&"sources/managed/Doing.Cli/Program.cs"));
        assert!(tracked_paths.contains(&"sources/managed/Doing.Cli/packages.lock.json"));
        assert!(
            !tracked_paths
                .iter()
                .any(|path| path.contains("/obj/") || path.contains("/bin/"))
        );

        Ok(())
    }

    #[test]
    fn rebuild_reason_detects_file_changes_and_missing_dll() {
        let tempdir = TempDir::new().expect("tempdir should be created");
        let existing_dll = tempdir.path().join("existing.dll");
        fs::write(&existing_dll, []).expect("existing dll should be written");

        let current = CacheSnapshot {
            version: CACHE_VERSION,
            built_at_utc: "2026-04-22T12:34:56Z".to_owned(),
            input_fingerprint: "new".to_owned(),
            files: vec![CacheFileRecord {
                path: "Program.cs".to_owned(),
                kind: "cs".to_owned(),
                hash: "new".to_owned(),
            }],
        };
        let cached = CacheSnapshot {
            version: CACHE_VERSION,
            built_at_utc: "2026-04-22T00:00:00Z".to_owned(),
            input_fingerprint: "old".to_owned(),
            files: vec![CacheFileRecord {
                path: "Program.cs".to_owned(),
                kind: "cs".to_owned(),
                hash: "old".to_owned(),
            }],
        };

        assert!(
            rebuild_reason(
                Some(&cached),
                &current,
                Path::new("/definitely/missing.dll")
            )
            .expect("missing dll should rebuild")
            .contains("does not exist")
        );
        assert_eq!(
            rebuild_reason(Some(&cached), &current, &existing_dll),
            Some("tracked input changed: Program.cs".to_owned())
        );
        assert_eq!(
            rebuild_reason(None, &current, &existing_dll),
            Some("cache.toml is missing or unreadable".to_owned())
        );
    }

    #[test]
    fn gitignore_check_detects_missing_cache_entry() -> Result<()> {
        let fixture = Fixture::new()?;
        fs::write(fixture.doing_dir.join(".gitignore"), "config.toml\n")?;

        assert_eq!(
            gitignore_status(&fixture.doing_dir),
            GitignoreStatus::MissingCacheEntry
        );

        Ok(())
    }

    #[test]
    fn launcher_builds_then_execs_and_reuses_cache() -> Result<()> {
        let fixture = Fixture::new()?;
        let dotnet = fixture.write_fake_dotnet()?;
        let start_dir = fixture.root.join("sources/managed/Doing.Cli");

        let first_exit = run_launcher(
            start_dir.clone(),
            vec![OsString::from("alpha"), OsString::from("--beta")],
            dotnet.as_os_str(),
        )?;
        assert_eq!(first_exit, 23);

        let first_log = fs::read_to_string(fixture.log_path())?;
        assert!(first_log.contains("CMD=build"));
        assert!(first_log.contains("CMD=exec"));
        assert!(first_log.contains("alpha --beta"));
        assert!(fixture.root.join(".doing/cache.toml").is_file());

        fs::write(fixture.log_path(), "")?;
        let second_exit =
            run_launcher(start_dir, vec![OsString::from("again")], dotnet.as_os_str())?;
        assert_eq!(second_exit, 23);

        let second_log = fs::read_to_string(fixture.log_path())?;
        assert!(!second_log.contains("CMD=build"));
        assert!(second_log.contains("CMD=exec"));
        assert!(second_log.contains(" again"));

        fs::write(
            fixture.root.join("Directory.Build.props"),
            "<Project><Changed/></Project>",
        )?;
        fs::write(fixture.log_path(), "")?;

        let third_exit = run_launcher(
            fixture.root.join("sources/managed/Doing.Cli"),
            vec![OsString::from("changed")],
            dotnet.as_os_str(),
        )?;
        assert_eq!(third_exit, 23);

        let third_log = fs::read_to_string(fixture.log_path())?;
        assert!(third_log.contains("CMD=build"));
        assert!(third_log.contains("CMD=exec"));
        assert!(third_log.contains(" changed"));

        Ok(())
    }

    struct Fixture {
        _tempdir: TempDir,
        root: PathBuf,
        doing_dir: PathBuf,
    }

    impl Fixture {
        fn new() -> Result<Self> {
            let tempdir = TempDir::new()?;
            let root = tempdir.path().to_path_buf();
            let doing_dir = root.join(".doing");

            fs::create_dir_all(doing_dir.clone())?;
            fs::create_dir_all(root.join("sources/managed/Doing.Cli/obj/Debug/net10.0"))?;
            fs::create_dir_all(root.join("sources/managed/Doing.Cli/bin/Release"))?;

            fs::write(
                doing_dir.join("config.toml"),
                r#"
version = 1
name = "Doing"
description = "A managed build runner powered by dotnet."
main_project = "sources/managed/Doing.Cli/Doing.Cli.csproj"
main_dll = "sources/managed/Doing.Cli/bin/Release/net10.0/Doing.Cli.dll"
solution = "Doing.slnx"
scan_roots = ["."]
extra_inputs = ["global.json", ".editorconfig"]
exclude_globs = ["**/bin/**", "**/obj/**", "**/.git/**", "**/.doing/**"]
"#,
            )?;
            fs::write(doing_dir.join(".gitignore"), "cache.toml\n")?;
            fs::write(root.join("global.json"), "{ }\n")?;
            fs::write(root.join(".editorconfig"), "root = true\n")?;
            fs::write(root.join("Directory.Build.props"), "<Project />\n")?;
            fs::write(root.join("Directory.Packages.props"), "<Project />\n")?;
            fs::write(root.join("Doing.slnx"), "<Solution />\n")?;
            fs::write(
                root.join("sources/managed/Doing.Cli/Doing.Cli.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\" />\n",
            )?;
            fs::write(
                root.join("sources/managed/Doing.Cli/Program.cs"),
                "internal static class Program { }\n",
            )?;
            fs::write(
                root.join("sources/managed/Doing.Cli/packages.lock.json"),
                "{ }\n",
            )?;

            Ok(Self {
                _tempdir: tempdir,
                root,
                doing_dir,
            })
        }

        fn log_path(&self) -> PathBuf {
            self.root.join("fake-dotnet.log")
        }

        fn write_fake_dotnet(&self) -> Result<PathBuf> {
            let script_path = self.root.join("fake-dotnet.sh");
            let main_dll = self
                .root
                .join("sources/managed/Doing.Cli/bin/Release/net10.0/Doing.Cli.dll");
            let log_path = self.log_path();

            let script = format!(
                "#!/bin/sh\n\
LOG=\"{}\"\n\
printf 'PWD=%s CMD=%s ARGS=' \"$PWD\" \"$1\" >> \"$LOG\"\n\
shift\n\
first=1\n\
for arg in \"$@\"; do\n\
  if [ \"$first\" -eq 1 ]; then\n\
    printf '%s' \"$arg\" >> \"$LOG\"\n\
    first=0\n\
  else\n\
    printf ' %s' \"$arg\" >> \"$LOG\"\n\
  fi\n\
done\n\
printf '\\n' >> \"$LOG\"\n\
case \"$0\" in\n\
  *) ;;\n\
esac\n\
case \"$(tail -n 1 \"$LOG\" | sed 's/^.*CMD=//; s/ ARGS=.*$//')\" in\n\
  build)\n\
    mkdir -p \"{}\"\n\
    : > \"{}\"\n\
    exit 0\n\
    ;;\n\
  exec)\n\
    exit 23\n\
    ;;\n\
esac\n\
exit 99\n",
                log_path.display(),
                main_dll
                    .parent()
                    .expect("main dll should have a parent")
                    .display(),
                main_dll.display()
            );

            fs::write(&script_path, script)?;
            let mut permissions = fs::metadata(&script_path)?.permissions();
            permissions.set_mode(0o755);
            fs::set_permissions(&script_path, permissions)?;

            Ok(script_path)
        }
    }
}
