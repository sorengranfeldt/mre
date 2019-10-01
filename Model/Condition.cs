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
    XmlInclude(typeof(SubCondition))
    ]
    public class ConditionBase
    {
        public string Description;

        public virtual bool Met(MVEntry mventry, CSEntry csentry)
        {
            return true;
        }
    }
    public class ConditionBetween : ConditionBase
    {
        public string MVAttributeStartDate;
        public string MVAttributeEndDate;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            if (!mventry[this.MVAttributeStartDate].IsPresent)
            {
                Tracer.TraceInformation("Condition failed (Reason: MVAttributeStartDate {0} attribute value is not present) / {0}", this.MVAttributeStartDate, this.Description);
                return false;
            }
            if (!mventry[this.MVAttributeEndDate].IsPresent)
            {
                Tracer.TraceInformation("Condition failed (Reason: MVAttributeEndDate {0} attribute value is not present) / {0}", this.MVAttributeEndDate, this.Description);
                return false;
            }

            DateTime startDate;
            DateTime endDate;
            if (!DateTime.TryParse(mventry[this.MVAttributeStartDate].StringValue, out startDate))
            {
                Tracer.TraceWarning("unable-to-parse-start-mvvalue-to-datetime {0}", mventry[this.MVAttributeStartDate].StringValue);
                return false;
            }
            if (!DateTime.TryParse(mventry[this.MVAttributeEndDate].StringValue, out endDate))
            {
                Tracer.TraceWarning("unable-to-parse-end-mvvalue-to-datetime {0}", mventry[this.MVAttributeEndDate].StringValue);
                return false;
            }

            DateTime now = DateTime.Now;
            bool returnValue = (startDate < now) && (now < endDate);
            Tracer.TraceInformation("compare-dates now: {0}, start: {1}, end: {2}, is-between: {3}", now, startDate, endDate, returnValue);
            return returnValue;
        }
    }

    public class ConditionAfter : ConditionBase
    {
        public string MVAttribute;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            if (mventry[this.MVAttribute].IsPresent)
            {
                DateTime value;
                if (DateTime.TryParse(mventry[this.MVAttribute].StringValue, out value))
                {
                    DateTime now = DateTime.Now;
                    bool returnValue = now > value;
                    Tracer.TraceInformation("compare-dates now: {0}, mvvalue: {1}, is-after: {2}", now, value, returnValue);
                    return returnValue;
                }
                else
                {
                    Tracer.TraceWarning("unable-to-parse-mvvalue-to-datetime {0}", mventry[this.MVAttribute].StringValue);
                    return false;
                }
            }
            else
            {
                Tracer.TraceInformation("Condition failed (Reason: Metaverse attribute value is not present) {0}", this.Description);
                return false;
            }
        }
    }
    public class ConditionBefore : ConditionBase
    {
        public string MVAttribute;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            if (mventry[this.MVAttribute].IsPresent)
            {
                DateTime value;
                if (DateTime.TryParse(mventry[this.MVAttribute].StringValue, out value))
                {
                    DateTime now = DateTime.Now;
                    bool returnValue = now < value;
                    Tracer.TraceInformation("compare-dates now: {0}, mvvalue: {1}, is-before: {2}", now, value, returnValue);
                    return returnValue;
                }
                else
                {
                    Tracer.TraceWarning("unable-to-parse-mvvalue-to-datetime {0}", mventry[this.MVAttribute].StringValue);
                    return false;
                }
            }
            else
            {
                Tracer.TraceInformation("Condition failed (Reason: Metaverse attribute value is not present) {0}", this.Description);
                return false;
            }
        }
    }

    public class ConditionIsPresent : ConditionBase
    {
        public string MVAttribute;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            if (mventry[this.MVAttribute].IsPresent)
            {
                return true;
            }
            else
            {
                Tracer.TraceInformation("Condition failed (Reason: Metaverse attribute value is not present) {0}", this.Description);
                return false;
            }
        }
    }
    public class ConditionIsNotPresent : ConditionBase
    {
        public string MVAttribute;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            if (!mventry[this.MVAttribute].IsPresent)
            {
                return true;
            }
            else
            {
                Tracer.TraceInformation("Condition failed (Reason: Metaverse attribute value is present) {0}", this.Description);
                return false;
            }
        }
    }

    public class ConditionMatch : ConditionBase
    {
        public string MVAttribute;
        public string Pattern;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            if (!mventry[this.MVAttribute].IsPresent)
            {
                Tracer.TraceInformation("Condition failed (Reason: No metaverse value is present) {0}", this.Description);
                return false;
            }
            else
            {
                if (!Regex.IsMatch(mventry[this.MVAttribute].Value, this.Pattern, RegexOptions.IgnoreCase))
                {
                    Tracer.TraceInformation("Condition failed (Reason: RegEx doesnt match) {0}", this.Description);
                    return false;
                }
                return true;
            }
        }
    }
    public class ConditionNotMatch : ConditionBase
    {
        public string MVAttribute;
        public string Pattern;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            if (mventry[this.MVAttribute].IsPresent)
            {
                if (Regex.IsMatch(mventry[this.MVAttribute].Value, this.Pattern, RegexOptions.IgnoreCase))
                {
                    Tracer.TraceInformation("Condition failed (Reason: RegEx match) {0}", this.Description);
                    return false;
                }
            }
            return true; // value not present effectively means not-match
        }
    }

    public class ConditionIsTrue : ConditionBase
    {
        public string MVAttribute;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            if (!mventry[this.MVAttribute].IsPresent)
            {
                Tracer.TraceInformation("Condition failed (Reason: No metaverse value is present) {0}", this.Description);
                return false;
            }

            if (!mventry[this.MVAttribute].BooleanValue)
            {
                Tracer.TraceInformation("Condition failed (Reason: Boolean value is false) {0}", this.Description);
                return false;
            }
            return true;
        }
    }

    public class ConditionContains : ConditionBase
    {
        public string MVAttribute;
        public string Pattern;
        public bool CaseSensitive;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            if (!mventry[this.MVAttribute].IsPresent)
            {
                Tracer.TraceInformation("Condition failed (Reason: No metaverse value is present) {0}", this.Description);
                return false;
            }

            if (mventry[this.MVAttribute].Values?.Count > 0)
            {
                var cmpOptions = StringComparison.CurrentCulture;
                if (!CaseSensitive) { cmpOptions = StringComparison.CurrentCultureIgnoreCase; }

                var entries = mventry[this.MVAttribute].Values.ToStringArray();
                return entries.Any(x => String.Equals(x, Pattern, cmpOptions));
            }
            else
            {
                Tracer.TraceInformation("Condition failed (Reason: Metaverse multivalue contains no elements) {0}", this.Description);
                return false;
            }
        }
    }

    public class ConditionNotContains : ConditionContains
    {
        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            return !base.Met(mventry, csentry);
        }
    }

    public class ConditionIsFalse : ConditionBase
    {
        public string MVAttribute;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            if (!mventry[this.MVAttribute].IsPresent)
            {
                Tracer.TraceInformation("Condition failed (Reason: No metaverse value is present) {0}", this.Description);
                return false;
            }

            if (mventry[this.MVAttribute].BooleanValue)
            {
                Tracer.TraceInformation("Condition failed (Reason: Boolean value is true) {0}", this.Description);
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Returns true if the metaverse attribute is not present or if present and not true
    /// </summary>
    public class ConditionIsNotTrue : ConditionBase
    {
        public string MVAttribute;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            if (mventry[this.MVAttribute].IsPresent)
            {
                if (mventry[this.MVAttribute].BooleanValue)
                {
                    Tracer.TraceInformation("Condition failed (Reason: Boolean value is true) {0}", this.Description);
                    return false;
                }
            }
            return true;
        }
    }

    public class ConditionAreEqual : ConditionBase
    {
        public string MVAttribute;
        public string CSAttribute;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            string csValue = csentry[CSAttribute].IsPresent ? csentry[CSAttribute].Value : null;
            string mvValue = mventry[MVAttribute].IsPresent ? mventry[MVAttribute].Value : null;

            if (csValue != mvValue)
            {
                Tracer.TraceInformation("Condition failed (Reason: Values are not equal) {0}", this.Description);
                return false;
            }
            else
            {
                return true;
            }
        }
    }

    public class ConditionIsDNEqual : ConditionBase
    {
        public string MVAttribute;
        public string CSAttribute;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            return ConditionIsDNEqual.Compare(mventry, csentry, this.CSAttribute, this.MVAttribute);
        }

        internal static bool Compare(MVEntry mventry, CSEntry csentry, string csattributeName, string mvattributeName)
        {
            ReferenceValue csval = null;
            ReferenceValue mvval = null;

            Attrib mvAttrib = mventry[mvattributeName];

            if (csattributeName == "[DN]")
            {
                csval = csentry.DN;
            }
            else
            {
                Attrib csAttrib = csentry[csattributeName];

                if (!csAttrib.IsPresent && !mvAttrib.IsPresent)
                {
                    return true;
                }

                if (csAttrib.IsPresent ^ mvAttrib.IsPresent)
                {
                    return false;
                }

                switch (csAttrib.DataType)
                {
                    case AttributeType.Reference:
                        csval = csAttrib.ReferenceValue;
                        break;

                    case AttributeType.String:
                        csval = csentry.MA.CreateDN(csAttrib.StringValue);
                        break;

                    default:
                        Tracer.TraceError("Can only compare string values as DNs");
                        return false;
                }
            }

            if (mvAttrib.DataType != AttributeType.String)
            {
                Tracer.TraceError("Can only compare string values as DNs");
            }

            mvval = csentry.MA.CreateDN(mvAttrib.StringValue);

            return mvval.Equals(csval);
        }
    }

    public class ConditionIsDNNotEqual : ConditionBase
    {
        public string MVAttribute;
        public string CSAttribute;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            return !ConditionIsDNEqual.Compare(mventry, csentry, this.CSAttribute, this.MVAttribute);
        }
    }


    public class ConditionAreNotEqual : ConditionBase
    {
        public string MVAttribute;
        public string CSAttribute;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            string csValue = csentry[CSAttribute].IsPresent ? csentry[CSAttribute].Value : null;
            string mvValue = mventry[MVAttribute].IsPresent ? mventry[MVAttribute].Value : null;

            if (csValue == mvValue)
            {
                Tracer.TraceInformation("Condition failed (Reason: Values are equal) {0}", this.Description);
                return false;
            }
            else
            {
                return true;
            }
        }
    }

    public class ConditionConnectedTo : ConditionBase
    {
        public string ManagementAgentName;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            ConnectedMA MA = mventry.ConnectedMAs[this.ManagementAgentName];
            if (MA.Connectors.Count.Equals(0))
            {
                Tracer.TraceInformation("Condition failed (Reason: Not connected to {0}) {1}", this.ManagementAgentName, this.Description);
                return false;
            }
            return true;
        }
    }
    public class ConditionNotConnectedTo : ConditionBase
    {
        public string ManagementAgentName;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            ConnectedMA MA = mventry.ConnectedMAs[this.ManagementAgentName];
            if (MA.Connectors.Count > 0)
            {
                Tracer.TraceInformation("Condition failed (Reason: Still connected to {0}) {1}", this.ManagementAgentName, this.Description);
                return false;
            }
            return true;
        }
    }

    public class SubCondition : ConditionBase
    {
        [XmlAttribute]
        [XmlTextAttribute()]
        public ConditionOperator Operator { get; set; }

        [XmlElement(ElementName = "ConditionBase")]
        public List<ConditionBase> ConditionBase { get; set; }

        public SubCondition()
        {
            this.ConditionBase = new List<ConditionBase>();
        }

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            Tracer.TraceInformation("enter-subconditionsmet");
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
                Tracer.TraceInformation("exit-subconditionsmet");
            }
        }
    }

    #region obsolete

    [Obsolete]
    public class ConditionAttributeIsPresent : ConditionBase
    {
        public string MVAttribute;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            Tracer.TraceWarning("Condition type 'ConditionAttributeIsPresent' is obsolete. Please see documentation.");
            if (mventry[this.MVAttribute].IsPresent)
            {
                return true;
            }
            else
            {
                Tracer.TraceInformation("Condition failed (Reason: Metaverse attribute value is present) {0}", this.Description);
                return false;
            }
        }
    }
    [Obsolete]
    public class ConditionAttributeIsNotPresent : ConditionBase
    {
        public string MVAttribute;

        public override bool Met(MVEntry mventry, CSEntry csentry)
        {
            Tracer.TraceWarning("Condition type 'ConditionAttributeIsNotPresent' is obsolete. Please see documentation.");
            if (!mventry[this.MVAttribute].IsPresent)
            {
                return true;
            }
            else
            {
                Tracer.TraceInformation("Condition failed (Reason: Metaverse attribute value is present) {0}", this.Description);
                return false;
            }
        }
    }

    #endregion

}

