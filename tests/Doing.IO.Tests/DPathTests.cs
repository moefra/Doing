// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using IOPath = global::System.IO.Path;

namespace Doing.IO.Tests;

public class DPathTests
{
    [Test]
    public async Task ImplicitConversions_WorkInBothDirections()
    {
        DPath dPath = "alpha/beta.txt";
        string value = dPath;

        await Assert.That(value).IsEqualTo($"alpha{IOPath.DirectorySeparatorChar}beta.txt");
    }

    [Test]
    public async Task Normalization_KeepsRelativePathsRelative()
    {
        var path = new DPath("alpha//beta/../gamma/./delta.txt");

        await Assert.That(path.Value).IsEqualTo($"alpha{IOPath.DirectorySeparatorChar}gamma{IOPath.DirectorySeparatorChar}delta.txt");
        await Assert.That(path.IsAbsolute).IsFalse();
    }

    [Test]
    public async Task Equality_UsesNormalizedPathComparer()
    {
        var left = new DPath("alpha//beta/../gamma.txt");
        var right = new DPath("alpha/gamma.txt");

        await Assert.That(left == right).IsTrue();
        await Assert.That(left.GetHashCode()).IsEqualTo(right.GetHashCode());
    }

    [Test]
    public async Task Equality_UsesPlatformSpecificCaseRules()
    {
        var left = new DPath("alpha/file.txt");
        var right = new DPath("ALPHA/FILE.txt");

        bool shouldMatch = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        await Assert.That(left.Equals(right)).IsEqualTo(shouldMatch);
    }

    [Test]
    public async Task PathProperties_ExposeNameStemExtensionAndParent()
    {
        var path = new DPath("alpha/beta/file.txt");

        await Assert.That(path.Name).IsEqualTo("file.txt");
        await Assert.That(path.Stem).IsEqualTo("file");
        await Assert.That(path.Extension).IsEqualTo(".txt");
        await Assert.That(path.Parent?.Value).IsEqualTo($"alpha{IOPath.DirectorySeparatorChar}beta");
    }

    [Test]
    public async Task CombineAndChangeExtension_ReturnNewPaths()
    {
        var basePath = new DPath("alpha");

        DPath combined = basePath.Combine("beta","file.json");
        DPath updated = combined.ChangeExtension(".yaml");

        await Assert.That(combined.Value).IsEqualTo($"alpha{IOPath.DirectorySeparatorChar}beta{IOPath.DirectorySeparatorChar}file.json");
        await Assert.That(updated.Value).IsEqualTo($"alpha{IOPath.DirectorySeparatorChar}beta{IOPath.DirectorySeparatorChar}file.yaml");
    }

    [Test]
    public async Task RelativeTo_ReturnsRelativePath()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            var basePath = new DPath(IOPath.Combine(tempRoot,"alpha"));
            var targetPath = new DPath(IOPath.Combine(tempRoot,"alpha","beta","file.txt"));

            DPath relative = targetPath.RelativeTo(basePath);

            await Assert.That(relative.Value).IsEqualTo($"beta{IOPath.DirectorySeparatorChar}file.txt");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Test]
    public async Task CreateDirectoryAndEnsureParentDirectory_CreateExpectedDirectories()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            DPath directoryDPath = new DPath(IOPath.Combine(tempRoot,"nested","folder"));
            DPath fileDPath = new DPath(IOPath.Combine(tempRoot,"files","item.txt"));

            directoryDPath.CreateDirectory();
            fileDPath.EnsureParentDirectory();

            await Assert.That(Directory.Exists(directoryDPath.Value)).IsTrue();
            await Assert.That(Directory.Exists(IOPath.Combine(tempRoot,"files"))).IsTrue();
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Test]
    public async Task ReadWriteTextAndBytes_RoundTripAndCreateParents()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            DPath textDPath = new DPath(IOPath.Combine(tempRoot,"texts","hello.txt"));
            DPath bytesDPath = new DPath(IOPath.Combine(tempRoot,"bytes","payload.bin"));

            textDPath.WriteText("hello world",Encoding.UTF8);
            bytesDPath.WriteBytes([1,2,3,4]);

            await Assert.That(textDPath.ReadText()).IsEqualTo("hello world");
            await Assert.That(Convert.ToHexString(await bytesDPath.ReadBytesAsync())).IsEqualTo("01020304");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Test]
    public async Task AsyncTextApis_RoundTrip()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            DPath textDPath = new DPath(IOPath.Combine(tempRoot,"async","hello.txt"));

            await textDPath.WriteTextAsync("async hello");

            await Assert.That(await textDPath.ReadTextAsync()).IsEqualTo("async hello");
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Test]
    public async Task CopyMoveAndDelete_WorkForFiles()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            DPath source = new DPath(IOPath.Combine(tempRoot,"source","file.txt"));
            DPath copy = new DPath(IOPath.Combine(tempRoot,"copy","file.txt"));
            DPath moved = new DPath(IOPath.Combine(tempRoot,"moved","file.txt"));

            source.WriteText("payload");
            source.CopyTo(copy);
            copy.MoveTo(moved);
            moved.Delete();

            await Assert.That(source.ReadText()).IsEqualTo("payload");
            await Assert.That(File.Exists(copy.Value)).IsFalse();
            await Assert.That(File.Exists(moved.Value)).IsFalse();
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Test]
    public async Task CopyMoveAndDelete_WorkForDirectories()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            DPath source = new DPath(IOPath.Combine(tempRoot,"source-dir"));
            DPath copy = new DPath(IOPath.Combine(tempRoot,"copy-dir"));
            DPath moved = new DPath(IOPath.Combine(tempRoot,"moved-dir"));

            source.CreateDirectory();
            new DPath(IOPath.Combine(source.Value,"nested","file.txt")).WriteText("payload");

            source.CopyTo(copy);
            copy.MoveTo(moved);
            moved.Delete(recursive: true);

            await Assert.That(File.Exists(IOPath.Combine(source.Value,"nested","file.txt"))).IsTrue();
            await Assert.That(Directory.Exists(copy.Value)).IsFalse();
            await Assert.That(Directory.Exists(moved.Value)).IsFalse();
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Test]
    public async Task JsonRoundTrip_WorksForSyncAndAsync()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            DPath jsonDPath = new DPath(IOPath.Combine(tempRoot,"models","sample.json"));
            var expected = new SampleDocument { Name = "Moe", Count = 7 };

            jsonDPath.WriteJsonFile(expected,new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            SampleDocument syncResult = jsonDPath.ReadJsonFile<SampleDocument>(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            await jsonDPath.WriteJsonFileAsync(expected);
            SampleDocument asyncResult = await jsonDPath.ReadJsonFileAsync<SampleDocument>();

            await Assert.That(syncResult.Name).IsEqualTo(expected.Name);
            await Assert.That(syncResult.Count).IsEqualTo(expected.Count);
            await Assert.That(asyncResult.Name).IsEqualTo(expected.Name);
            await Assert.That(asyncResult.Count).IsEqualTo(expected.Count);
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Test]
    public async Task YamlRoundTrip_WorksForSyncAndAsync()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            DPath yamlDPath = new DPath(IOPath.Combine(tempRoot,"models","sample.yaml"));
            var expected = new SampleDocument { Name = "Moe", Count = 7 };

            yamlDPath.WriteYamlFile(expected);
            SampleDocument syncResult = yamlDPath.ReadYamlFile<SampleDocument>();

            await yamlDPath.WriteYamlFileAsync(expected);
            SampleDocument asyncResult = await yamlDPath.ReadYamlFileAsync<SampleDocument>();

            await Assert.That(syncResult.Name).IsEqualTo(expected.Name);
            await Assert.That(syncResult.Count).IsEqualTo(expected.Count);
            await Assert.That(asyncResult.Name).IsEqualTo(expected.Name);
            await Assert.That(asyncResult.Count).IsEqualTo(expected.Count);
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Test]
    public async Task TomlRoundTrip_WorksForSyncAndAsync()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            DPath tomlDPath = new DPath(IOPath.Combine(tempRoot,"models","sample.toml"));
            var expected = new SampleDocument { Name = "Moe", Count = 7 };

            tomlDPath.WriteTomlFile(expected);
            SampleDocument syncResult = tomlDPath.ReadTomlFile<SampleDocument>();

            await tomlDPath.WriteTomlFileAsync(expected);
            SampleDocument asyncResult = await tomlDPath.ReadTomlFileAsync<SampleDocument>();

            await Assert.That(syncResult.Name).IsEqualTo(expected.Name);
            await Assert.That(syncResult.Count).IsEqualTo(expected.Count);
            await Assert.That(asyncResult.Name).IsEqualTo(expected.Name);
            await Assert.That(asyncResult.Count).IsEqualTo(expected.Count);
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    [Test]
    public async Task InvalidSerializationContent_Throws()
    {
        string tempRoot = CreateTempDirectory();

        try
        {
            DPath jsonDPath = new DPath(IOPath.Combine(tempRoot,"broken","sample.json"));
            DPath yamlDPath = new DPath(IOPath.Combine(tempRoot,"broken","sample.yaml"));
            DPath tomlDPath = new DPath(IOPath.Combine(tempRoot,"broken","sample.toml"));

            jsonDPath.WriteText("{ invalid json");
            yamlDPath.WriteText("key: [1, 2");
            tomlDPath.WriteText("value = [");

            Exception? jsonException = await CaptureExceptionAsync(() => Task.FromResult(jsonDPath.ReadJsonFile<SampleDocument>()));
            Exception? yamlException = await CaptureExceptionAsync(() => Task.FromResult(yamlDPath.ReadYamlFile<SampleDocument>()));
            Exception? tomlException = await CaptureExceptionAsync(() => Task.FromResult(tomlDPath.ReadTomlFile<SampleDocument>()));

            await Assert.That(jsonException is JsonException).IsTrue();
            await Assert.That(yamlException).IsNotNull();
            await Assert.That(tomlException).IsNotNull();
        }
        finally
        {
            DeleteDirectoryIfExists(tempRoot);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = IOPath.Combine(IOPath.GetTempPath(),$"doing-io-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path,true);
        }
    }

    private static async Task<Exception?> CaptureExceptionAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private sealed class SampleDocument
    {
        public string Name { get; set; } = string.Empty;

        public int Count { get; set; }
    }
}
