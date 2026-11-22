namespace Rukbat
{
    internal sealed partial class Emitter
    {
        private void EmitHttpLoader(RuntimeUsage usage)
        {
            EmitNativeLoader(RuntimeCatalog.Native.Http, assemblyName: RuntimeCatalog.Native.HttpAssembly);
            if (usage.UsesHttpBodyType)
            {
                // Function parameters or variables may use [RukbatHttpBody] before the first network operation.
                Line($"& $__rukbat_load_{RuntimeCatalog.Native.Http}");
            }
        }

        private string HttpCall(Call call, string operation) =>
            AdaptedStandardCall(call, operation, RuntimeCatalog.Modules.Http, StandardFieldLayout.Positional);
    }
}
