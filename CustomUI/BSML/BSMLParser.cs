﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace CustomUI.BSML
{
    internal class BSMLParser
    {
        private Assembly owner;
        private XmlDocument tree;
        private string StartingNamespace;

        internal XmlDocument Doc => tree;

        private BSMLParser(Assembly owner, string startingNamespace, XmlDocument tree)
        {
            this.tree = tree;
            this.owner = owner;
            StartingNamespace = startingNamespace;
        }

        public static BSMLParser LoadFrom(Assembly owner, string startingNamespace, TextReader reader)
        {
            var xml = new XmlDocument();
            xml.Load(reader);
            return new BSMLParser(owner, startingNamespace, xml);
        }


        public IElement Parse()
        { // seperate because there are only a handful of valid top-level elements that should be processed


            return null;
        }

        private Type GetController(IEnumerable<Attribute> attrs, Type currentOwner)
        {
            var controller = attrs.FirstOrDefault(a => a.Type == AttributeType.Literal
                                               && a.NameSpace == BSML.CoreNamespace
                                               && a.Name == "controller");

            if (controller == null) return null;

            var value = controller.LiteralValue;
            if (value.StartsWith("."))
            { // subtype
                var ctrlType = currentOwner?.GetNestedType(value.Substring(1), BindingFlags.NonPublic | BindingFlags.Public);
                if (ctrlType == null) throw new TypeLoadException($"Could not find type {currentOwner?.FullName}/{value}");
                return ctrlType;
            }
            else
            { // non-subtype
                var dotIndex = value.IndexOf('.');

                if (dotIndex == -1)
                { // non-qualified name
                    // search same namespace in owning assembly, or starting namespace in owning assembly
                    var type = owner.GetTypes()
                                    .Where(t => t.Namespace == (currentOwner?.Namespace ?? StartingNamespace))
                                    .FirstOrDefault(t => t.Name == value);
                    if (type == null)
                        throw new TypeLoadException($"Could not find type {value}");

                    return type;
                }
                else
                { // its a fully-qualified type name
                    return owner.GetType(value, true, false);
                }
            }
        }

        internal IEnumerable<Attribute> GetAttributes(XmlElement element, ref Type controllerType, bool allowElementAttributes = true)
        {
            var attrs = new List<Attribute>();

            foreach (var attr in element.Attributes.Cast<XmlAttribute>())
                attrs.Add(new Attribute(this, attr, controllerType));

            var newType = GetController(attrs, controllerType);
            if (newType != null) controllerType = newType;

            if (allowElementAttributes)
            {
                foreach (var node in element.ChildNodes.Cast<XmlNode>())
                    if (node is XmlElement el && Attribute.IsElementAttribute(el))
                        attrs.Add(new Attribute(this, el, controllerType));
            }

            return attrs;
        }

        // Text elements have their own IElement type
        internal IEnumerable<IElement> ReadTree(IEnumerable<XmlNode> elements, Type owningType)
        {
            return new IElement[0];
        }
    }
}