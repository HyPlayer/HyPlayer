using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;

namespace HyPlayer.NeteaseApi.Generator
{
    public class JsonContextReceiver : ISyntaxReceiver
    {
        public List<ClassDeclarationSyntax> TargetClass { get; private set; } = new List<ClassDeclarationSyntax>();
        public void OnVisitSyntaxNode(SyntaxNode context)
        {
            if (context is ClassDeclarationSyntax cds)
            {
                var types = cds.BaseList?.Types;
                if (types != null)
                {

                    foreach (var item in types)
                    {
                        if (item.ToString() == "CodedResponseBase")
                        {
                            TargetClass.Add(cds);
                        }
                    }
                }
            }
        }
    }
}
