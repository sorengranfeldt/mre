namespace Granfeldt
{
    using Microsoft.MetadirectoryServices;
    using System;

    public class AttributeFlowGuid : AttributeFlowBase
    {
        public override void Generate(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule rule)
        {
            Tracer.TraceInformation("enter-attributeflowguid");
            base.Generate(ma, csentry, mventry, rule);
            try
            {
                Guid newGuid = Guid.NewGuid();
                Tracer.TraceInformation("new-guid-'{0}'-to-'{1}'", newGuid.ToString(), this.Target);

                if (this.Target.Equals("[DN]", StringComparison.OrdinalIgnoreCase))
                    csentry.DN = csentry.MA.CreateDN(newGuid.ToString());
                else
                    csentry[this.Target].Value = newGuid.ToString();
            }
            catch (Exception ex)
            {
                Tracer.TraceError("error {0}", ex.GetBaseException());
                throw;
            }
            finally
            {
                Tracer.TraceInformation("exit-attributeflowguid");
            }
        }
    }
}
