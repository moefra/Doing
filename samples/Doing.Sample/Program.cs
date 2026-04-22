
using Doing.Core;

namespace Doing.Sample;

class Program : DoingBuild
{
    public static void Main(string[] args) =>
        Doing<Program>(args);

    public Target Zoo => New().Name("a").Description("");


}
