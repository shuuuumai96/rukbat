namespace Rukbat
{
    internal sealed partial class Emitter
    {
        private void EmitHttpLoader()
        {
            EmitNativeLoader("Http", "System.Net.Http");
            // Function parameters may use Body before the first network operation.
            Line("& $__rukbat_load_Http");
        }

        private string HttpCall(Call call, string operation) =>
            AdaptedStandardCall(call, operation, "http", StandardFieldLayout.Positional);
    }
}
