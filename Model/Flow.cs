namespace Granfeldt
{
	using Microsoft.MetadirectoryServices;
	using System;
	using System.Diagnostics;
	using System.Text.RegularExpressions;
	using System.Xml.Serialization;

	[
	XmlInclude(typeof(AttributeFlowAttribute)),
	XmlInclude(typeof(AttributeFlowConcatenate)),
	XmlInclude(typeof(AttributeFlowConstant)),
	XmlInclude(typeof(AttributeFlowGuid)),
	XmlInclude(typeof(Flow)),
	]
	public class AttributeFlowBase
	{
		public string Description;
		public string Target;

		public virtual void Generate(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule rule)
		{
			//intentionally left blank
		}
	}

	public class AttributeFlowConcatenate : AttributeFlowBase
	{
		public SourceExpressionBase[] SourceExpressions;

		public override void Generate(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule rule)
		{
			Trace.TraceInformation("enter-attributeflowconcatenate");
			Trace.Indent();
			base.Generate(ma, csentry, mventry, rule);
			try
			{
				string concatValue = null;
				foreach (SourceExpressionBase sourceExpression in this.SourceExpressions)
				{
					if (sourceExpression.GetType() == typeof(SourceExpressionConstant))
					{
						SourceExpressionConstant sourceExpr = (SourceExpressionConstant)sourceExpression;
						string replacedValue = sourceExpr.Source.ReplaceWithMVValueOrBlank(mventry);
						Trace.TraceInformation("adding-constant-'{0}'", replacedValue);
						concatValue = concatValue + replacedValue;
						continue;
					}
					if (sourceExpression.GetType() == typeof(SourceExpressionRegexReplace))
					{
						SourceExpressionRegexReplace sourceExpr = (SourceExpressionRegexReplace)sourceExpression;
						Trace.TraceInformation("adding-regex-replacement-'{0}'", sourceExpr.Source);
						if (mventry[sourceExpr.Source].IsPresent)
						{
							concatValue = concatValue + Regex.Replace(mventry[sourceExpr.Source].Value, sourceExpr.Pattern, sourceExpr.Replacement);
						}
						else
						{
							Trace.TraceError("attribute-'{0}'-is-not-present-in-metaverse", sourceExpr.Source);
						}
						continue;
					}
					if (sourceExpression.GetType() == typeof(SourceExpressionAttribute))
					{
						SourceExpressionAttribute attr = (SourceExpressionAttribute)sourceExpression;
						if (mventry[attr.Source].IsPresent)
						{
							Trace.TraceInformation("adding-value-from-MV::'{0}'-to-'{1}'", attr.Source, mventry[attr.Source].Value);
							concatValue = concatValue + mventry[attr.Source].Value.ToString();
						}
						else
						{
							Trace.TraceError("attribute-'{0}'-is-not-present-in-metaverse", attr.Source);
						}
						continue;
					}
				}
				Trace.TraceInformation("flow-concatenated-attribute-value-'{0}'-to-'{1}'", concatValue, this.Target);
				if (this.Target.Equals("[DN]", StringComparison.OrdinalIgnoreCase))
					csentry.DN = csentry.MA.CreateDN(concatValue);
				else
					csentry[(this.Target)].Value = concatValue;
			}
			catch (Exception ex)
			{
				Trace.TraceError("error {0}", ex.GetBaseException());
				throw;
			}
			finally
			{
				Trace.Unindent();
				Trace.TraceInformation("exit-attributeflowconcatenate");
			}
		}
	}

	public class AttributeFlowConstant : AttributeFlowBase
	{
		public string EscapedCN;
		public string Constant;

		public override void Generate(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule rule)
		{
			Trace.TraceInformation("enter-attributeflowconstant");
			Trace.Indent();
			base.Generate(ma, csentry, mventry, rule);
			try
			{
				string escapedCN = null;
				string replacedValue = null;
				replacedValue = this.Constant.ReplaceWithHelperValuesOrBlank(rule.Helpers);

				if (string.IsNullOrEmpty(this.EscapedCN))
				{
					Trace.TraceInformation("no-CN-to-escape");
					replacedValue = replacedValue.ReplaceWithMVValueOrBlank(mventry);
				}
				else
				{
					escapedCN = this.EscapedCN.ReplaceWithHelperValuesOrBlank(rule.Helpers);
					escapedCN = ma.EscapeDNComponent(this.EscapedCN.ReplaceWithMVValueOrBlank(mventry, "")).ToString();
					Trace.TraceInformation("escaped-cn '{0}'", escapedCN);
					replacedValue = replacedValue.ReplaceWithMVValueOrBlank(mventry, escapedCN);
				}
				Trace.TraceInformation("flow-constant-'{0}'-to-'{1}'", replacedValue, this.Target);
				if (this.Target.Equals("[DN]", StringComparison.OrdinalIgnoreCase))
					csentry.DN = csentry.MA.CreateDN(replacedValue);
				else
					csentry[(this.Target)].Value = replacedValue;
			}
			catch (Exception ex)
			{
				Trace.TraceError("error {0}", ex.GetBaseException());
				throw;
			}
			finally
			{
				Trace.Unindent();
				Trace.TraceInformation("exit-attributeflowconstant");
			}
		}
	}

	public class AttributeFlowGuid : AttributeFlowBase
	{
		public override void Generate(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule rule)
		{
			Trace.TraceInformation("enter-attributeflowguid");
			Trace.Indent();
			base.Generate(ma, csentry, mventry, rule);
			try
			{
				Guid newGuid = Guid.NewGuid();
				Trace.TraceInformation("new-guid-'{0}'-to-'{1}'", newGuid.ToString(), this.Target);

				if (this.Target.Equals("[DN]", StringComparison.OrdinalIgnoreCase))
					csentry.DN = csentry.MA.CreateDN(newGuid.ToString());
				else
					csentry[this.Target].Value = newGuid.ToString();
			}
			catch (Exception ex)
			{
				Trace.TraceError("error {0}", ex.GetBaseException());
				throw;
			}
			finally
			{
				Trace.Unindent();
				Trace.TraceInformation("exit-attributeflowguid");
			}
		}
	}

	public class AttributeFlowAttribute : AttributeFlowBase
	{
		public string Prefix;
		public string Source;
		public bool LowercaseTargetValue;
		public bool UppercaseTargetValue;
		public bool TrimTargetValue;

		public override void Generate(ConnectedMA ma, CSEntry csentry, MVEntry mventry, Rule rule)
		{
			Trace.TraceInformation("enter-attributeflowattribute");
			Trace.Indent();
			base.Generate(ma, csentry, mventry, rule);
			try
			{
				string TargetValue;
				if (mventry[this.Source].DataType == AttributeType.Binary)
				{
					TargetValue = BitConverter.ToString(mventry[this.Source].BinaryValue);
					TargetValue = TargetValue.Replace("-", "");
				}
				else
				{
					TargetValue = mventry[this.Source].Value;
				}
				if (this.LowercaseTargetValue) { TargetValue = TargetValue.ToLower(); }
				if (this.UppercaseTargetValue) { TargetValue = TargetValue.ToUpper(); }
				if (this.TrimTargetValue) { TargetValue = TargetValue.Trim(); }

				TargetValue = (string.IsNullOrEmpty(this.Prefix)) ? TargetValue : this.Prefix + TargetValue;

				Trace.TraceInformation("flow-source-value: '{0}'", mventry[this.Source].Value);
				Trace.TraceInformation("target-value: '{0}'", TargetValue);
				if (this.Target.Equals("[DN]", StringComparison.OrdinalIgnoreCase))
					csentry.DN = csentry.MA.CreateDN(TargetValue);
				else
					csentry[this.Target].Value = TargetValue;
			}
			catch (Exception ex)
			{
				Trace.TraceError("error {0}", ex.GetBaseException());
				throw;
			}
			finally
			{
				Trace.Unindent();
				Trace.TraceInformation("exit-attributeflowattribute");
			}
		}
	}

	public class Flow : AttributeFlowBase
	{
		// undocumented for this version. Reserved for future versions 
		// and will replace AttributeFlowBase eventually
	}

	#region source expressions for concatenate
	[XmlInclude(typeof(SourceExpressionRegexReplace)), XmlInclude(typeof(SourceExpressionConstant)), XmlInclude(typeof(SourceExpressionAttribute))]
	public class SourceExpressionBase
	{
		public string Source;
	}
	public class SourceExpressionAttribute : SourceExpressionBase
	{
	}
	public class SourceExpressionConstant : SourceExpressionBase
	{
	}
	public class SourceExpressionRegexReplace : SourceExpressionBase
	{
		public string Pattern;
		public string Replacement;
	}

	#endregion
}
