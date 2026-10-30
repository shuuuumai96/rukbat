namespace Rukbat
{
    internal sealed partial class Emitter
    {
        private void EmitCsvRuntime()
        {
            EmitNativeDefinition("Csv");
            EmitLines(RuntimeResources.ReadPowerShell("CsvHelpers"));
        }
    }
}
