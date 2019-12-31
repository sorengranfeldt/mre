namespace Granfeldt
{
    using Microsoft.MetadirectoryServices;

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
}
