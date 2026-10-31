namespace Rukbat
{
    internal sealed partial class Emitter
    {
        private void EmitCliRuntime() => EmitLines(RuntimeResources.ReadPowerShell("CliHelpers"));

        private string CliCall(Call call, string operation) =>
            AdaptedStandardCall(call, operation, "cli", StandardFieldLayout.Named);
    }
}
