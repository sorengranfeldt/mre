namespace Granfeldt
{
    using Microsoft.MetadirectoryServices;
    
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
}
