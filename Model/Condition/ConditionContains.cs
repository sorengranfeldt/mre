namespace Granfeldt
{
    using Microsoft.MetadirectoryServices;
    using System;
    using System.Linq;

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

}
