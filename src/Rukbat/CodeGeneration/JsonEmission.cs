namespace Rukbat;

internal sealed partial class Emitter
{
    private void EmitJsonRuntime()
    {
        // Preserve the existing JSON helper indentation in generated scripts/debug maps.
        EmitNativeDefinition("Json", "        ");
        EmitLines(RuntimeResources.ReadPowerShell("JsonRuntime"));
    }
}
