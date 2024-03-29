﻿using FastExpressionCompiler;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;

using static System.Linq.Expressions.Expression;

namespace CustomUI.BSML
{
    /// <summary>
    /// The type of an <see cref="Attribute"/>.
    /// </summary>
    public enum AttributeType
    {
        /// <summary>
        /// The attribute holds a literal string value.
        /// </summary>
        Literal = 0x1,
        /// <summary>
        /// The attribute is a binding of some kind. Never seen on its own.
        /// </summary>
        Binding = 0x2,
        /// <summary>
        /// The attribute is an input binding. It takes the result of the field/property
        /// specified as its value, which may be gotten repeatedly.
        /// </summary>
        InputBinding = Binding | 0x4,
        /// <summary>
        /// The attribute is an output binding. It puts some value in the field/property
        /// specified, which may be set repeatedly.
        /// </summary>
        OutputBinding = Binding | 0x8,
        /// <summary>
        /// The attribute is a function binding. This is used for events.
        /// </summary>
        FunctionBinding = Binding | 0x10,
        /// <summary>
        /// The attribute represents a self ref. This is an <see cref="OutputBinding"/> that 
        /// gets the value of the element the attribute is attached to.
        /// </summary>
        SelfRef = OutputBinding | 0x1,

        /// <summary>
        /// The attribute is an element-type attribute. It has child elements and attributes.
        /// This is used for more complex properties.
        /// </summary>
        /// <note>
        /// The namespace of an element attribute will always be the same as the namespace as
        /// the element it is an attribute for.
        /// </note>
        ElementAttribute = 0x20
    }

    /// <summary>
    /// A BSML Attribute.
    /// </summary>
    public class Attribute
    {
        /// <summary>
        /// The XML namespace the attribute was defined with.
        /// </summary>
        public string NameSpace { get; private set; }
        /// <summary>
        /// The name of the attribute.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// The type of attribute. Determines which fields are valid.
        /// </summary>
        public AttributeType Type { get; private set; }

        /// <summary>
        /// The literal value of the attribute. In the case of bindings, it will be the name of the
        /// field/property/method referenced. 
        /// </summary>
        public string LiteralValue { get; private set; }

        /// <summary>
        /// A getter delegate type.
        /// </summary>
        /// <param name="target">the binding object source.</param>
        /// <returns>the binding value</returns>
        public delegate object GetBinding(object target);
        /// <summary>
        /// A setter delegate type.
        /// </summary>
        /// <param name="target">the binding object source.</param>
        /// <param name="value">the binding value</param>
        public delegate void SetBinding(object target, object value);

        /// <summary>
        /// The type that all bindings reference. Always valid.
        /// </summary>
        public Type LinkedType { get; private set; }
        /// <summary>
        /// The type of the binding. Only valid for bindings.
        /// </summary>
        public Type BindingType { get; private set; }

        /// <summary>
        /// The getter for an <see cref="AttributeType.InputBinding"/>.
        /// </summary>
        public GetBinding BindingGetter { get; private set; }
        /// <summary>
        /// The setter for an <see cref="AttributeType.OutputBinding"/>.
        /// </summary>
        public SetBinding BindingSetter { get; private set; }

        /// <summary>
        /// The <see cref="MethodInfo"/> for the function binding. Only valid on <see cref="AttributeType.FunctionBinding"/>
        /// </summary>
        public MethodInfo FunctionBinding { get; private set; }

        /// <summary>
        /// The attributes of this element attribute. Only valid on <see cref="AttributeType.ElementAttribute"/>.
        /// </summary>
        public Attribute[] ElementAttributes { get; private set; }

        /// <summary>
        /// The content of this element attribute. Only valid on <see cref="AttributeType.ElementAttribute"/>.
        /// </summary>
        public Element[] ElementContent { get; private set; }

        internal delegate void AttributeVisitor(AttributeType type, string coreValue);

        internal static void VisitAttributeValueType(string value, string nameSpace, string name, AttributeVisitor visit)
        {
            value = value.Trim();

            if (value[0] == '{' && value[value.Length - 1] == '}')
            {
                //Type = AttributeType.Binding;
                var trimmed = value.Substring(1, value.Length - 2).Trim();

                var type = AttributeType.Binding;
                if (trimmed[0] == '=') type = AttributeType.InputBinding;
                if (trimmed[trimmed.Length - 1] == '=') type = AttributeType.OutputBinding;

                if (type == AttributeType.InputBinding || type == AttributeType.OutputBinding)
                {
                    value = trimmed.Trim('=').Trim();

                    if (type == AttributeType.OutputBinding && nameSpace == BSML.CoreNamespace && name == "ref")
                    {
                        type = AttributeType.SelfRef;
                    }

                    visit(type, value);
                }
                else if (trimmed.EndsWith("()"))
                {
                    value = trimmed.Trim('(',')').Trim();
                    type = AttributeType.FunctionBinding;

                    visit(type, value);
                }
            }
            else
            {
                visit(AttributeType.Literal, value);
            }
        }

        internal Attribute(BSMLParser parser, XmlAttribute attr, Type connectedType)
        {
            Logger.log.Debug($"Processing attribute {attr.OuterXml} for {connectedType} on {attr?.OwnerElement?.Name} {attr?.OwnerElement?.NamespaceURI}");

            Name = attr.LocalName;
            NameSpace = attr.NamespaceURI;
            LinkedType = connectedType;
            LiteralValue = attr.Value.Trim();

            VisitAttributeValueType(LiteralValue, NameSpace, Name, (ty, val) =>
            {
                LiteralValue = val;
                Type = ty;

                if (Type == AttributeType.InputBinding)
                {
                    var selfParam = Parameter(typeof(object), "self");

                    var propExpr =
                        PropertyOrField(
                            Convert(
                                selfParam,
                                connectedType
                            ),
                            LiteralValue
                        );

                    BindingType = propExpr.Type;

                    BindingGetter = Lambda<GetBinding>(
                            Convert(propExpr, typeof(object)),
                            selfParam
                        ).CompileFast();
                }

                if (Type == AttributeType.OutputBinding || Type == AttributeType.SelfRef)
                {
                    var selfParam = Parameter(typeof(object), "self");
                    var valueParam = Parameter(typeof(object), "value");

                    var propExpr =
                        PropertyOrField(
                            Convert(
                                selfParam,
                                connectedType
                            ),
                            LiteralValue
                        );

                    BindingType = propExpr.Type;

                    BindingSetter = Lambda<SetBinding>(
                            Assign(
                                propExpr,
                                Convert(
                                    valueParam,
                                    BindingType
                                )
                            ),
                            selfParam,
                            valueParam
                        ).CompileFast();
                }

                if (Type == AttributeType.FunctionBinding)
                {
                    FunctionBinding = connectedType.GetMethod(val, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
            });

            if (Type == AttributeType.Binding)
                throw new InvalidProgramException("Invalid binding type!");

            if (NameSpace == BSML.CoreNamespace && Name == "ref" && Type != AttributeType.SelfRef)
                throw new InvalidProgramException("'ref' parameter MUST be an OutputBinding");
        }

        internal Attribute(BSMLParser parser, XmlElement elem, BSMLParser.ParseState state, Element[] children)
        {
            if (!IsElementAttribute(elem))
                throw new ArgumentException("When an Attribute is constructed with an Element, is MUST be a valid Element attribute", nameof(elem));

            Logger.log.Debug($"Processing element attribute {elem?.Name} {elem?.NamespaceURI} for {state.Type} on {elem?.ParentNode?.Name} {elem?.ParentNode?.NamespaceURI}");

            Name = elem.LocalName.Split('.').Last();
            NameSpace = elem.NamespaceURI;
            LinkedType = state.Type;

            Type = AttributeType.ElementAttribute;

            ElementAttributes = parser.GetAttributes(elem, ref state, out var hasController, false).ToArray();

            if (hasController)
                throw new InvalidProgramException("Cannot have a controller attribute on an element attribute");

            ElementContent = children;
            //ElementContent = parser.ReadTree(elem.ChildNodes.Cast<XmlNode>(), connectedType).ToArray();
        }

        internal static bool IsElementAttribute(XmlElement elem)
        {
            var spl = elem.LocalName.Split('.');
            return spl.Length == 2 && spl[0] == elem.ParentNode.LocalName && elem.NamespaceURI == elem.ParentNode.NamespaceURI;
        }
    }
}
