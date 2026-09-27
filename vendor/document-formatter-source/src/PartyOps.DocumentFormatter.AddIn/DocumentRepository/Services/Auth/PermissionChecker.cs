using DocumentRepository.Models.Features;

namespace DocumentRepository.Services.Auth;

public static class PermissionChecker
{
	public static bool CanExecute(FeatureDescriptor feature)
	{
		return feature != null;
	}

	public static bool IsMembershipExpired()
	{
		return false;
	}

	public static bool CanManageRules()
	{
		return true;
	}

	public static string GetRuleManagementDeniedMessage(string featureName, string actionName)
	{
		return "当前规则管理暂时不可用，请重新打开窗口后重试。";
	}
}
