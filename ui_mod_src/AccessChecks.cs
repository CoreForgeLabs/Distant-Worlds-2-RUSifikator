using System;
using System.Runtime.CompilerServices;

// The mod is compiled against a copy of DistantWorlds.Types.dll with every member made public
// (work/ref, see tools/ilrebuild --publicize). This attribute tells the runtime to allow the same
// access against the real assembly, so game internals can be used as in the game's own code.
[assembly: IgnoresAccessChecksTo("DistantWorlds.Types")]
[assembly: IgnoresAccessChecksTo("DistantWorlds.UI")]

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    internal sealed class IgnoresAccessChecksToAttribute : Attribute
    {
        public IgnoresAccessChecksToAttribute(string assemblyName) { AssemblyName = assemblyName; }
        public string AssemblyName { get; }
    }
}
