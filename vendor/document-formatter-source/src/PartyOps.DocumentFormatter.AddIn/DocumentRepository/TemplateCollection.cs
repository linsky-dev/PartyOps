using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace DocumentRepository;

[Serializable]
public class TemplateCollection
{
	public int CurrentIndex { get; set; }

	[XmlArray("Templates")]
	[XmlArrayItem("Template")]
	public List<FormatConfig> Templates { get; set; }

	[XmlArray("TemplateNames")]
	[XmlArrayItem("Name")]
	public List<string> TemplateNames { get; set; }

	public TemplateCollection()
	{
		CurrentIndex = 0;
		Templates = new List<FormatConfig>();
		TemplateNames = new List<string>();
	}
}
