namespace Granfeldt
{
    using Microsoft.MetadirectoryServices;
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
}
