namespace Granfeldt
{
    using Microsoft.MetadirectoryServices;
    using System.Text.RegularExpressions;

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
}
