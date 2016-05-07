// february 26, 2015 | soren granfeldt
//  - marked ConditionAttributeIsPresent and ConditionAttributeNotIsPresent as obsolete
// march 7, 2015 | soren granfeldt
//  -added options for future use of operators and/or on conditions
// april 4, 2015 | soren granfeldt
//	-added Trace logging to all functions

namespace Granfeldt
{
	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.IO;
	using System.Xml.Serialization;

	public static class MVRules
	{
		#region Methods

		public static void LoadSettingsFromFile(string Filename, ref RulesFile Rules)
		{
			Tracer.TraceInformation("enter-loadsettingsfromfile");
			try
			{
				XmlSerializer serializer = new XmlSerializer(typeof(RulesFile));
				StreamReader textReader = new StreamReader(Filename);
				Rules = (RulesFile)serializer.Deserialize(textReader);
				textReader.Close();
			}
			catch (Exception ex)
			{
				Tracer.TraceError("error {0}", ex.GetBaseException());
				throw;
			}
			finally
			{
				Tracer.Unindent();
				Tracer.TraceInformation("exit-loadsettingsfromfile");
			}
		}
		public static void SaveRulesConfigurationFile(ref RulesFile F, string Filename)
		{
			Tracer.TraceInformation("enter-saverulesconfigurationfile");
			try
			{
				XmlSerializer serializer = new XmlSerializer(typeof(RulesFile));
				StreamWriter writer = new StreamWriter(Filename, false);
				serializer.Serialize((TextWriter)writer, F);
				writer.Close();
			}
			catch (Exception ex)
			{
				Tracer.TraceError("error {0}", ex.GetBaseException());
				throw;
			}
			finally
			{
				Tracer.Unindent();
				Tracer.TraceInformation("exit-saverulesconfigurationfile");
			}
		}

		#endregion
	}

	public class RulesFile
	{
		public List<Rule> Rules;
		public RulesFile()
		{
			this.Rules = new List<Rule>();
		}
	}


}
