// october 12, 2015 | soren granfeldt
//	- added for future implementation

namespace Granfeldt
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.Linq;
	using System.Text;
	using System.Threading.Tasks;
	using System.Xml.Serialization;

	[XmlInclude(typeof(HelperValueConstant)), XmlInclude(typeof(HelperValueScopedGuid))]
	public class HelperValue
	{
		[XmlAttribute]
		[XmlTextAttribute()]
		public string Name;
		public virtual string GetValue { get; }
		public virtual void Generate()
		{
			Trace.TraceInformation("helper-value name: {0}, value: {1}", Name, GetValue);
		}
	}
	public class HelperValueScopedGuid : HelperValue
	{
		private Guid guid;
		public override string GetValue
		{
			get
			{
				return this.guid.ToString();
			}
		}
		public override void Generate()
		{
			this.guid = Guid.NewGuid();
			base.Generate();
		}
	}
	public class HelperValueConstant : HelperValue
	{
		public string Value;
		public override string GetValue
		{
			get
			{
				return this.Value;
			}
		}
		public override void Generate()
		{
			base.Generate();
		}
	}

}
