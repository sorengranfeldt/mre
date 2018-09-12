// september 12, 2018 | soren granfeldt
//	-initial version

using Microsoft.MetadirectoryServices;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;

namespace Granfeldt
{
    public enum ExternalType
    {
        [XmlEnum("Provision")]
        Provision,
        [XmlEnum("ShouldDeleteFromMV")]
        ShouldDeleteFromMV,

    }
	public class External
	{
        [XmlIgnore]
        Assembly Assembly = null;
        [XmlIgnore]
        IMVSynchronization instance = null;
        internal string fullFilename;

		public string ReferenceId;
        public ExternalType Type;
		public string Filename;

        public string FilenameFull
        {
            get
            {
                return fullFilename;
            }
        }
        internal void EnsureFullFilename(string ruleFilesPath)
        {
            fullFilename = System.IO.Path.Combine(ruleFilesPath, this.Filename);
        }
        public void LoadExternalAssembly()
        {
            if (string.IsNullOrEmpty(this.FilenameFull))
            {
                return;
            }
            Tracer.TraceInformation("enter-loadexternalassembly");
            try
            {
                {
                    Tracer.TraceInformation("loading-loadexternalassembly {0}", Path.Combine(Utils.ExtensionsDirectory, this.FilenameFull));
                    this.Assembly = Assembly.LoadFile(FilenameFull);
                    Type[] types = Assembly.GetExportedTypes();
                    Type type = types.Where(u => u.GetInterface("Microsoft.MetadirectoryServices.IMVSynchronization") != null).FirstOrDefault();
                    if (type != null)
                    {
                        instance = Activator.CreateInstance(type) as IMVSynchronization;
                    }
                    else
                    {
                        Tracer.TraceError("interface-not-implemented {0}", "Microsoft.MetadirectoryServices.IMVSynchronization");
                        throw new NotImplementedException("interface-not-implemented");
                    }
                }
            }
            catch (Exception ex)
            {
                Tracer.TraceError("loadexternalassembly {0}", ex.GetBaseException());
                throw;
            }
            finally
            {
                Tracer.TraceInformation("exit-loadexternalassembly");
            }
        }

        public void Initialize()
        {
            Tracer.TraceInformation("invoke-initialize: reference-id: {0}, filename: {1}", ReferenceId, FilenameFull);
            instance.Initialize();
        }
        public void Terminate()
        {
            Tracer.TraceInformation("invoke-terminate: reference-id: {0}, filename: {1}", ReferenceId, FilenameFull);
            instance.Terminate();
        }
        public void Provision(MVEntry mventry)
        {
            Tracer.TraceInformation("invoke-provision: reference-id: {0}, filename: {1}", ReferenceId, FilenameFull);
            instance.Provision(mventry);
        }
        public void ShouldDeleteFromMV(CSEntry csentry, MVEntry mventry)
        {
            Tracer.TraceInformation("invoke-shoulddeletefrommv: reference-id: {0}, filename: {1}", ReferenceId, FilenameFull);
            instance.ShouldDeleteFromMV(csentry, mventry);
        }
    }
}
