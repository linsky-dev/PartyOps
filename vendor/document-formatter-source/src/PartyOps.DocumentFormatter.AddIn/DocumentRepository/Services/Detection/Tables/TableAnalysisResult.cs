using System.Collections.Generic;

namespace DocumentRepository.Services.Detection.Tables;

public class TableAnalysisResult
{
	public int TableCount { get; set; }

	public List<TableElementInfo> Tables { get; private set; } = new List<TableElementInfo>();

	public bool HasTables
	{
		get
		{
			if (TableCount <= 0)
			{
				return Tables.Count > 0;
			}
			return true;
		}
	}
}
