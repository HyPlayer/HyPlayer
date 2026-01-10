using Microsoft.CodeAnalysis;
using System;
using System.Text;

namespace HyPlayer.NeteaseApi.Generator
{
    [Generator]
    public class JsonContextSourceGenerator : ISourceGenerator
    {
        public void Execute(GeneratorExecutionContext context)
        {
            //return;
            // Find the main method
            if (context.SyntaxReceiver is JsonContextReceiver receiver)
            {

                var sb = new StringBuilder();
                foreach (var item in receiver.TargetClass)
                {
                    var typeName = item.Identifier;
                    sb.Append($"[JsonSerializable(typeof({typeName}))]\r\n");
                }
                // Build up the source code
                string source = $@"using System.Text.Json.Serialization;
using HyPlayer.NeteaseApi.ApiContracts;

namespace HyPlayer.NeteaseApi.Serialization
{{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    {sb.ToString()}
    public partial class JsonSerializeContext : JsonSerializerContext
    {{
    }}
}}
";


                // Add the source code to the compilation
                context.AddSource($"JsonSerializeContext.cs", source);
            }
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new JsonContextReceiver());
        }
    }
}
