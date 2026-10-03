using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace DocumentRepository.Models.RedHeader;

[Serializable]
public class RedHeaderTemplateSet
{
	public string ActiveTemplateId { get; set; }

	[XmlArray("Templates")]
	[XmlArrayItem("Template")]
	public List<RedHeaderTemplate> Templates { get; set; }

	public RedHeaderTemplateSet()
	{
		Templates = new List<RedHeaderTemplate>();
	}
}
