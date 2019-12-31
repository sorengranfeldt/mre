namespace Granfeldt
{
    using Microsoft.MetadirectoryServices;
    using System.Text.RegularExpressions;

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
}
