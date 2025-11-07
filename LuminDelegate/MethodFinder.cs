namespace LuminDelegates;

using System;
using System.Reflection;

public static class MethodFinder
{
    public static MethodInfo GetConcreteMethod(
        Type declaringType,
        string name,
        ReadOnlySpan<Type> argTypes)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Instance | BindingFlags.Static;
        
        int argCount = argTypes.Length;
        
        foreach (MethodInfo method in declaringType.GetMethods(flags))
        {
            if (method.Name != name) continue;
        
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != argCount) continue;

            MethodInfo ready = method;
            
            if (method.IsGenericMethodDefinition)
            {
                if (!TryInferGenericMethod(method, parameters, argTypes, out ready))
                    continue;
            }
            
            if (ParameterTypesMatch(ready.GetParameters(), argTypes))
                return ready;
        }
    
        return null;
    }

    private static bool TryInferGenericMethod(
        MethodInfo genericMethod,
        ParameterInfo[] parameters,
        ReadOnlySpan<Type> argTypes,
        out MethodInfo concreteMethod)
    {
        Type[] genericArgs = genericMethod.GetGenericArguments();
        Type[] inferredTypes = new Type[genericArgs.Length];
        
        for (int i = 0; i < genericArgs.Length; i++)
        {
            Type genericArg = genericArgs[i];
            Type inferredType = null;
            
            for (int j = 0; j < parameters.Length; j++)
            {
                if (parameters[j].ParameterType == genericArg)
                {
                    inferredType = argTypes[j];
                    break;
                }
            }
        
            if (inferredType == null)
            {
                concreteMethod = null;
                return false;
            }
        
            inferredTypes[i] = inferredType;
        }
    
        try
        {
            concreteMethod = genericMethod.MakeGenericMethod(inferredTypes);
            return true;
        }
        catch
        {
            concreteMethod = null;
            return false;
        }
    }

    private static bool ParameterTypesMatch(ParameterInfo[] parameters, ReadOnlySpan<Type> argTypes)
    {
        int length = parameters.Length;
        for (int i = 0; i < length; i++)
        {
            if (parameters[i].ParameterType != argTypes[i])
                return false;
        }
        return true;
    }
    
    public static MethodInfo GetConcreteMethod(
        Type declaringType,
        string name,
        params Type[] argTypes)
    {
        return GetConcreteMethod(declaringType, name, argTypes.AsSpan());
    }
}