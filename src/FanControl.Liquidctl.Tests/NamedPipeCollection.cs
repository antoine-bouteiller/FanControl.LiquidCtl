using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace FanControl.LiquidCtl.Tests;

// Serializes every class that touches the OS-global "LiquidCtlPipe" name.
// xUnit parallelizes across classes, so without this a PipeTransport connect
// races a LiquidctlClient one-shot server and both flake.
[CollectionDefinition("NamedPipe", DisableParallelization = true)]
[SuppressMessage("Design", "CA1515", Justification = "xUnit collection definitions must be public to be discovered")]
public sealed class NamedPipeSuite;
