namespace Granfeldt
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using System.Text;
	using System.Threading.Tasks;

	public class RenameAction
	{
		public string EscapedCN;
		/// <summary>
		/// Use #mv:??# notation
		/// </summary>
		public string NewDNValue;
		/// <summary>
		/// Specify metaverse attribute name or [DN]
		/// </summary>
		public string DNAttribute;
		/// <summary>
		/// Indicates whether the rename evaluation compares the old and new values as a plain string or an RFC-compliant DN
		/// </summary>
		public bool StrictDNCompare = false;
		/// <summary>
		/// Conditions that must be met for renaming to take place
		/// </summary>
		public Conditions Conditions;

		public RenameAction()
		{
			this.Conditions = new Conditions();
		}
	}

	[Obsolete("Use RenameAction instead")]
	public class RenameDnFlow
	{
		public bool ReprovisionOnRename = false;
		public string Source = null;
		public string Target = null;

		public bool OnlyRenameOnRegularExpressionMatch = false;
		public string RegExMVAttributeName = null;
		public string RegExFilter = null;

		public bool SourceValueIsPresent()
		{
			if (this.Source == null)
			{
				return false;
			}
			if (string.IsNullOrEmpty(this.Source))
			{
				return false;
			}
			return true;
		}
		public bool TargetValueIsPresent()
		{
			if (this.Target == null)
			{
				return false;
			}
			if (string.IsNullOrEmpty(this.Target))
			{
				return false;
			}
			return true;
		}
	}
}
