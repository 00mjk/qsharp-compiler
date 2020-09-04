// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Quantum.QsCompiler;
using Microsoft.Quantum.QsCompiler.DataTypes;
using Microsoft.Quantum.QsCompiler.SyntaxTokens;
using Microsoft.Quantum.QsCompiler.SyntaxTree;
using Microsoft.Quantum.QsCompiler.Transformations;
using Microsoft.Quantum.QsCompiler.Transformations.Core;
using Microsoft.Quantum.QsCompiler.Transformations.QsCodeOutput;
using YamlDotNet.Serialization;
using Range = Microsoft.Quantum.QsCompiler.DataTypes.Range;
using ResolvedTypeKind = Microsoft.Quantum.QsCompiler.SyntaxTokens.QsTypeKind<
    Microsoft.Quantum.QsCompiler.SyntaxTree.ResolvedType,
    Microsoft.Quantum.QsCompiler.SyntaxTree.UserDefinedType,
    Microsoft.Quantum.QsCompiler.SyntaxTree.QsTypeParameter,
    Microsoft.Quantum.QsCompiler.SyntaxTree.CallableInformation
>;

#nullable enable

namespace Microsoft.Quantum.Documentation
{
    internal interface IAttributeBuilder<T>
    {
        public IAttributeBuilder<T> AddAttribute(QsDeclarationAttribute attribute);
        public QsNullable<QsLocation> Location { get; }

        public T Build();
    }

    internal class Callable : IAttributeBuilder<QsCallable>
    {
        private QsCallable callable;
        internal Callable(QsCallable callable)
        {
            this.callable = callable;
        }

        public QsNullable<QsLocation> Location => callable.Location;

        public IAttributeBuilder<QsCallable> AddAttribute(QsDeclarationAttribute attribute) =>
            new Callable(callable.AddAttribute(attribute));

        public QsCallable Build() => callable;
    }

    internal class Udt : IAttributeBuilder<QsCustomType>
    {
        private QsCustomType type;
        internal Udt(QsCustomType type)
        {
            this.type = type;
        }

        public QsNullable<QsLocation> Location => type.Location;

        public IAttributeBuilder<QsCustomType> AddAttribute(QsDeclarationAttribute attribute) =>
            new Udt(type.AddAttribute(attribute));

        public QsCustomType Build() => type;
    }

    internal static class Extensions
    {
        internal static IAttributeBuilder<QsCallable> AttributeBuilder(
            this QsCallable callable
        ) => new Callable(callable);
        internal static IAttributeBuilder<QsCustomType> AttributeBuilder(
            this QsCustomType type
        ) => new Udt(type);

        internal static IAttributeBuilder<T> WithAttribute<T>(
            this IAttributeBuilder<T> builder, string @namespace, string name,
            TypedExpression input
        ) =>
            builder.AddAttribute(
                AttributeUtils.BuildAttribute(
                    new QsQualifiedName(
                        NonNullable<string>.New(@namespace),
                        NonNullable<string>.New(name)
                    ),
                    input
                )
            );

        private static IAttributeBuilder<T> WithDocumentationAttribute<T>(
            this IAttributeBuilder<T> builder, string attributeName,
            TypedExpression input
        ) => builder.WithAttribute("Microsoft.Quantum.Documentation", attributeName, input);

        private static TypedExpression AsLiteralExpression(this string literal) =>
            SyntaxGenerator.StringLiteral(
                NonNullable<string>.New(literal),
                ImmutableArray<TypedExpression>.Empty
            );

        internal static IAttributeBuilder<T> MaybeWithSimpleDocumentationAttribute<T>(
            this IAttributeBuilder<T> builder, string attributeName, string? value
        ) =>
            value == null || value.Trim().Length == 0
            ? builder
            : builder.WithDocumentationAttribute(
                attributeName, value.AsLiteralExpression()
            );

        internal static IAttributeBuilder<T> WithListOfDocumentationAttributes<T>(
            this IAttributeBuilder<T> builder, string attributeName, IEnumerable<string> items
        ) =>
            items
            .Aggregate(
                builder,
                (acc, item) => acc.WithDocumentationAttribute(
                    attributeName, item.AsLiteralExpression()
                )
            );

        internal static IAttributeBuilder<T> WithDocumentationAttributesFromDictionary<T>(
            this IAttributeBuilder<T> builder, string attributeName, IDictionary<string, string> items
        ) =>
            items
            .Aggregate(
                builder,
                (acc, item) => acc.WithDocumentationAttribute(
                    attributeName,
                    // The following populates all of the metadata needed for a
                    // Q# literal of type (String, String).
                    new TypedExpression(
                        QsExpressionKind<TypedExpression, Identifier, ResolvedType>.NewValueTuple(
                            ImmutableArray.Create(
                                item.Key.AsLiteralExpression(),
                                item.Value.AsLiteralExpression()
                            )
                        ),
                        ImmutableArray<Tuple<QsQualifiedName, NonNullable<string>, ResolvedType>>.Empty,
                        ResolvedType.New(
                            QsTypeKind<ResolvedType, UserDefinedType, QsTypeParameter, CallableInformation>.NewTupleType(
                                ImmutableArray.Create(
                                    ResolvedType.New(QsTypeKind<ResolvedType, UserDefinedType, QsTypeParameter, CallableInformation>.String),
                                    ResolvedType.New(QsTypeKind<ResolvedType, UserDefinedType, QsTypeParameter, CallableInformation>.String)
                                )
                            )
                        ),
                        new InferredExpressionInformation(false, false),
                        QsNullable<Range>.Null
                    )
                )
            );

        internal static string ToSyntax(this ResolvedType type) =>
            SyntaxTreeToQsharp.Default.ToCode(type);

        internal static string ToSyntax(this QsTypeItem item) =>
            item switch
            {
                QsTypeItem.Anonymous anon => anon.Item.ToSyntax(),
                QsTypeItem.Named named => $"{named.Item.VariableName.Value} : {named.Item.Type.ToSyntax()}"
            };
        
        internal static string ToSyntax(this QsTuple<QsTypeItem> items) =>
            items switch
            {
                QsTuple<QsTypeItem>.QsTuple tuple => $@"({
                    String.Join(", ", tuple.Item.Select(innerItem => innerItem.ToSyntax()))
                })",
                QsTuple<QsTypeItem>.QsTupleItem item => item.Item.ToSyntax()
            };

        internal static string ToSyntax(this QsTuple<LocalVariableDeclaration<QsLocalSymbol>> items) =>
            items switch
            {
                QsTuple<LocalVariableDeclaration<QsLocalSymbol>>.QsTuple tuple => $@"({
                    String.Join(", ", tuple.Item.Select(innerItem => innerItem.ToSyntax()))
                })",
                QsTuple<LocalVariableDeclaration<QsLocalSymbol>>.QsTupleItem item => item.Item.ToSyntax()
            };

        internal static string ToSyntax(this LocalVariableDeclaration<QsLocalSymbol> symbol) =>
            $@"{symbol.VariableName switch
            {
                QsLocalSymbol.ValidName name => name.Item.Value,
                _ => "{{invalid}}"
            }} : {symbol.Type.ToSyntax()}";

        internal static string ToSyntax(this QsCustomType type) =>
            $@"newtype {type.FullName.Name.Value} = {
                String.Join(",", type.TypeItems.ToSyntax())
            };";

        internal static string ToSyntax(this ResolvedCharacteristics characteristics) =>
            characteristics.SupportedFunctors.ValueOr(null) switch
            {
                null => "",
                { Count: 0 } => "",
                var functors => $@" is {String.Join(" + ", 
                    functors.Select(functor => functor.Tag switch
                    {
                        QsFunctor.Tags.Adjoint => "Adj",
                        QsFunctor.Tags.Controlled => "Ctl"
                    })
                )}"
            };

        internal static string ToSyntax(this QsCallable callable)
        {
            var kind = callable.Kind.Tag switch
            {
                QsCallableKind.Tags.Function => "function",
                QsCallableKind.Tags.Operation => "operation",
                QsCallableKind.Tags.TypeConstructor => "function"
            };
            var modifiers = callable.Modifiers.Access.Tag switch
            {
                AccessModifier.Tags.DefaultAccess => "",
                AccessModifier.Tags.Internal => "internal "
            };
            var typeParameters = callable.Signature.TypeParameters switch
            {
                { Length: 0 } => "",
                var typeParams => $@"<{
                    String.Join(", ", typeParams.Select(
                        param => param switch
                        {
                            QsLocalSymbol.ValidName name => $"'{name.Item.Value}",
                            _ => "{invalid}"
                        }
                    ))
                }>"
            };
            var input = callable.ArgumentTuple.ToSyntax();
            var output = callable.Signature.ReturnType.ToSyntax();
            var characteristics = callable.Signature.Information.Characteristics.ToSyntax();
            return $"{modifiers}{kind} {callable.FullName.Name.Value}{typeParameters}{input} : {output}{characteristics}";
        }

        internal static string MaybeWithSection(this string document, string name, string? contents) =>
            contents == null || contents.Trim().Length == 0
            ? document
            : $"{document}\n\n## {name}\n\n{contents}";

        internal static string WithYamlHeader(this string document, object header) =>
            $"---\n{new SerializerBuilder().Build().Serialize(header)}---\n{document}";

        internal static bool IsDeprecated(this QsCallable callable, out string? replacement) =>
            callable.Attributes.IsDeprecated(out replacement);

        internal static bool IsDeprecated(this QsCustomType type, out string? replacement) =>
            type.Attributes.IsDeprecated(out replacement);

        internal static bool IsDeprecated(this IEnumerable<QsDeclarationAttribute> attributes, [NotNullWhen(true)]  out string? replacement)
        {
            var deprecationAttribute = attributes.SingleOrDefault(attribute =>
            {
                if (attribute.TypeId.IsValue)
                {
                    var attrType = attribute.TypeId.Item;
                    if (attrType.Namespace.Value == "Microsoft.Quantum.Core" && attrType.Name.Value == "Deprecated")
                    {
                        return true;
                    }
                }

                return false;
            });

            if (deprecationAttribute == null)
            {
                replacement = null;
                return false;
            }
            else
            {
                var arg = deprecationAttribute.Argument.Expression as QsExpressionKind<TypedExpression, Identifier, ResolvedType>.StringLiteral;
                Debug.Assert(arg != null, "Argument to deprecated attribute was not a string literal.");
                replacement = arg.Item1.Value;
                return true;
            }

        }

        internal static Dictionary<string, ResolvedType> ToDictionaryOfDeclarations(this QsTuple<LocalVariableDeclaration<QsLocalSymbol>> items) =>
            items.InputDeclarations().ToDictionary(
                declaration => declaration.Item1,
                declaration => declaration.Item2
            );

        internal static Dictionary<string, ResolvedType> ToDictionaryOfDeclarations(this QsTuple<QsTypeItem> typeItems) =>
            typeItems.TypeDeclarations().ToDictionary(
                declaration => declaration.Item1,
                declaration => declaration.Item2
            );

        private static List<(string, ResolvedType)> TypeDeclarations(this QsTuple<QsTypeItem> typeItems) => typeItems switch
            {
                QsTuple<QsTypeItem>.QsTuple tuple =>
                    tuple.Item.SelectMany(
                        item => item.TypeDeclarations()
                    )
                    .ToList(),
                QsTuple<QsTypeItem>.QsTupleItem item => item.Item switch
                    {
                        QsTypeItem.Anonymous _ => new List<(string, ResolvedType)>(),
                        QsTypeItem.Named named => 
                        new List<(string, ResolvedType)>
                        {(
                            named.Item.VariableName.Value,
                            named.Item.Type
                        )}
                    }
            };

        private static List<(string, ResolvedType)> InputDeclarations(this QsTuple<LocalVariableDeclaration<QsLocalSymbol>> items) => items switch
            {
                QsTuple<LocalVariableDeclaration<QsLocalSymbol>>.QsTuple tuple =>
                    tuple.Item.SelectMany(
                        item => item.InputDeclarations()
                    )
                    .ToList(),
                QsTuple<LocalVariableDeclaration<QsLocalSymbol>>.QsTupleItem item =>
                    new List<(string, ResolvedType)>
                    {
                        (
                            item.Item.VariableName switch
                            {
                                QsLocalSymbol.ValidName name => name.Item.Value,
                                _ => "__invalid__"
                            },
                            item.Item.Type
                        )
                    }
            };

        internal static string ToMarkdownLink(this ResolvedType type) => type.Resolution switch
            {
                ResolvedTypeKind.ArrayType array => $"{array.Item.ToMarkdownLink()}[]",
                ResolvedTypeKind.Function function =>
                    $"{function.Item1.ToMarkdownLink()} -> {function.Item2.ToMarkdownLink()}",
                ResolvedTypeKind.Operation operation =>
                    $@"{operation.Item1.Item1.ToMarkdownLink()} => {operation.Item1.Item2.ToMarkdownLink()} {
                        operation.Item2.Characteristics.ToSyntax()
                    }",
                ResolvedTypeKind.TupleType tuple => "(" + String.Join(",",
                        tuple.Item.Select(ToMarkdownLink)
                    ) + ")",
                ResolvedTypeKind.UserDefinedType udt => udt.Item.ToMarkdownLink(),
                _ => type.Resolution.Tag switch
                {
                    ResolvedTypeKind.Tags.BigInt => "BigInt",
                    ResolvedTypeKind.Tags.Bool => "Bool",
                    ResolvedTypeKind.Tags.Double => "Double",
                    ResolvedTypeKind.Tags.Int => "Int",
                    ResolvedTypeKind.Tags.Pauli => "Pauli",
                    ResolvedTypeKind.Tags.Qubit => "Qubit",
                    ResolvedTypeKind.Tags.Range => "Range",
                    ResolvedTypeKind.Tags.String => "String",
                    ResolvedTypeKind.Tags.UnitType => "Unit",
                    _ => "__invalid__"
                }
            };

        internal static string ToMarkdownLink(this UserDefinedType type) =>
            $"[{type.Name.Value}](xref:{type.Namespace.Value}.{type.Name.Value})";

        internal static bool IsInCompilationUnit(this QsNamespaceElement element) =>
            element switch
            {
                QsNamespaceElement.QsCallable callable => callable.Item.IsInCompilationUnit(),
                QsNamespaceElement.QsCustomType type => type.Item.IsInCompilationUnit(),
                _ => false
            };

        internal static bool IsInCompilationUnit(this QsCallable callable) =>
            callable.SourceFile.Value.EndsWith(".qs");

        internal static bool IsInCompilationUnit(this QsCustomType type) =>
            type.SourceFile.Value.EndsWith(".qs");
    }

}