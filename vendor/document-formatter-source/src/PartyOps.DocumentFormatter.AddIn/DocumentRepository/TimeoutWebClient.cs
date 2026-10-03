using System;
using System.Net;

namespace DocumentRepository;

internal class TimeoutWebClient : WebClient
{
	public int Timeout { get; set; } = 5000;

	protected override WebRequest GetWebRequest(Uri uri)
	{
		WebRequest webRequest = base.GetWebRequest(uri);
		webRequest.Timeout = Timeout;
		return webRequest;
	}
}
