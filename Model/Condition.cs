// march 2, 2016 | soren granfeldt
// -added ConditionIsNotTrue

namespace Granfeldt
{
    using Microsoft.MetadirectoryServices;
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using System.Xml.Serialization;
    using System.Linq;

    public enum ConditionOperator
    {
        [XmlEnum("And")]
        And,
        [XmlEnum("Or")]
        Or
    }

    public class Conditions
    {
        [XmlAttribute]
        [XmlTextAttribute()]
        public ConditionOperator Operator { get; set; }
        [XmlElement]
        public List<ConditionBase> ConditionBase { get; set; }

        public Conditions()
        {
            this.ConditionBase = new List<ConditionBase>();
        }

        /// <summary>
        /// Evaluates true if the conditions are met for the given inputs.
        /// </summary>
        /// <param name="mventry">Metaverse operand</param>
        /// <param name="csentry">Connectorspace operand</param>
        /// <returns></returns>
        public virtual bool Met(MVEntry mventry, CSEntry csentry)
        {
            Tracer.TraceInformation("enter-conditionsmet");
            try
            {
                if (Operator.Equals(ConditionOperator.And))
                {
                    bool met = true;
                    foreach (ConditionBase condition in ConditionBase)
                    {
                        met = condition.Met(mventry, csentry);
                        Tracer.TraceInformation("'And' condition '{0}'/{1} returned: {2}", condition.GetType().Name, condition.Description, met);
                        if (met == false) break;
                    }
                    Tracer.TraceInformation("All 'And' conditions {0} met", met ? "were" : "were not");
                    return met;
                }
                else
                {
                    bool met = false;
                    foreach (ConditionBase condition in ConditionBase)
                    {
                        met = condition.Met(mventry, csentry);
                        Tracer.TraceInformation("'Or' condition '{0}'/{1} returned: {2}", condition.GetType().Name, condition.Description, met);
                        if (met == true) break;
                    }
                    Tracer.TraceInformation("One or more 'Or' conditions {0} met", met ? "were" : "were not");
                    return met;
                }
            }
            catch (Exception ex)
            {
                Tracer.TraceError("error {0}", ex.GetBaseException());
                throw;
            }
            finally
            {
                Tracer.TraceInformation("exit-conditionsmet");
            }
        }
    }

#pragma warning disable 0612, 0618
    [
    XmlInclude(typeof(ConditionAttributeIsNotPresent)), XmlInclude(typeof(ConditionAttributeIsPresent)),
    XmlInclude(typeof(ConditionMatch)), XmlInclude(typeof(ConditionNotMatch)),
    XmlInclude(typeof(ConditionConnectedTo)), XmlInclude(typeof(ConditionNotConnectedTo)),
    XmlInclude(typeof(ConditionIsPresent)), XmlInclude(typeof(ConditionIsNotPresent)),
    XmlInclude(typeof(ConditionAreEqual)), XmlInclude(typeof(ConditionAreNotEqual)),
    XmlInclude(typeof(ConditionIsTrue)), XmlInclude(typeof(ConditionIsFalse)),
    XmlInclude(typeof(ConditionContains)), XmlInclude(typeof(ConditionNotContains)),
    XmlInclude(typeof(ConditionIsNotTrue)),
    XmlInclude(typeof(ConditionIsDNEqual)),
    XmlInclude(typeof(ConditionIsDNNotEqual)),
    XmlInclude(typeof(ConditionAfter)), XmlInclude(typeof(ConditionBefore)), XmlInclude(typeof(ConditionBetween)),
    XmlInclude(typeof(SubCondition)),
    XmlInclude(typeof(ConditionBitIsSet)),
    XmlInclude(typeof(ConditionBitIsNotSet))
    ]
    public class ConditionBase
    {
        public string Description;

        public virtual bool Met(MVEntry mventry, CSEntry csentry)
        {
            return true;
        }
    }
}