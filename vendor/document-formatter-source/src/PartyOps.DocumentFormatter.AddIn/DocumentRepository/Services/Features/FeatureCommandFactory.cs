using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using DocumentRepository.Commands;
using DocumentRepository.Models.Features;

namespace DocumentRepository.Services.Features;

public static class FeatureCommandFactory
{
	public static ICommand CreateByFeatureId(string featureId)
	{
		return Create(FeatureRegistry.GetById(featureId));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ICommand Create(FeatureDescriptor descriptor)
	{
		if (descriptor != null)
		{
			if (string.IsNullOrWhiteSpace(descriptor.CommandClassName))
			{
				throw new InvalidOperationException("Feature command class name cannot be empty: " + descriptor.Id);
			}
			Type type = ResolveCommandType(descriptor.CommandClassName.Trim());
			if (!(type == null))
			{
				if (!typeof(ICommand).IsAssignableFrom(type))
				{
					throw new InvalidOperationException("Feature command class does not implement ICommand: " + descriptor.Id + " -> " + descriptor.CommandClassName);
				}
				try
				{
					return (ICommand)Activator.CreateInstance(type);
				}
				catch (MissingMethodException innerException)
				{
					throw new InvalidOperationException("Feature command class must provide a parameterless constructor: " + descriptor.Id + " -> " + descriptor.CommandClassName, innerException);
				}
			}
			throw new InvalidOperationException("Feature command class does not exist: " + descriptor.Id + " -> " + descriptor.CommandClassName);
		}
		throw new ArgumentNullException("descriptor");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static Type ResolveCommandType(string className)
	{
		Assembly assembly = typeof(ICommand).Assembly;
		if (className.IndexOf(".", StringComparison.Ordinal) >= 0)
		{
			return assembly.GetType(className, throwOnError: false);
		}
		return assembly.GetType("DocumentRepository.Commands." + className, throwOnError: false);
	}
}
