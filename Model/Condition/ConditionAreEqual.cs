namespace Granfeldt
{
    using Microsoft.MetadirectoryServices;

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

}
