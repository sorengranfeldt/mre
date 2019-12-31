// october 30, 2015 | soren granfeldt
//	-added possibility to use MV ObjectID in flow attribute

namespace Granfeldt
{
    using Microsoft.MetadirectoryServices;
    using System;
    using System.Diagnostics;
    using System.Text.RegularExpressions;
    using System.Xml.Serialization;
    using System.Collections.Generic;

    [
    XmlInclude(typeof(AttributeFlowAttribute)),
    XmlInclude(typeof(AttributeFlowConcatenate)),
    XmlInclude(typeof(AttributeFlowConstant)),
    XmlInclude(typeof(AttributeFlowMultivaluedConstant)),
    XmlInclude(typeof(AttributeFlowGuid)),
    XmlInclude(typeof(Flow)),
    ]
    public class AttributeFlowBase
    {
        public string Description;
        public string Target;

        public virtual void Generate(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule rule)
        {
            //intentionally left blank
        }
    }

    public class Flow : AttributeFlowBase
    {
        // undocumented for this version. Reserved for future versions 
        // and will replace AttributeFlowBase eventually
    }


}
