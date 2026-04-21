// Copyright (c) 2026 MoeGodot<me@kawayi.moe>.
// Licensed under the GNU Affero General Public License v3-or-later license.

namespace Doing.Core;

public static class CoreDoingBuildExtension
{
    extension(CoreDoingBuild build)
    {
        public Target Target(string name, string description)
        {
            return new Target(build.TaskSet, name, description);
        }
    }
}
