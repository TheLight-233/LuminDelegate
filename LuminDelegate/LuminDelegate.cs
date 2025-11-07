using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace LuminDelegates;

public static partial class LuminDelegate
{
    public static readonly ConcurrentDictionary<int, MethodCache> Methods = 
        new ConcurrentDictionary<int, MethodCache>();

    internal static bool IsIl2Cpp { get; private set; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SwitchToIl2Cpp()
    {
        IsIl2Cpp = true;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SwitchModel(bool isIl2Cpp)
    {
        IsIl2Cpp = isIl2Cpp;
    }

    #region Extensions
    
    // 将 Delegate 转换为 LuminAction<TTarget>
    public static LuminAction<TTarget> AsLuminAction<TTarget>(this Delegate @delegate) 
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
#endif
        => LuminAction<TTarget>.Create(@delegate); 
    
    // 将 Delegate 转换为 LuminFunction<TTarget, TResult>
    public static LuminFunction<TTarget, TResult> AsLuminFunction<TTarget, TResult>(this Delegate @delegate) 
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
#endif
        => LuminFunction<TTarget, TResult>.Create(@delegate); 
    
    
    // Action 类型的扩展方法
    public static LuminAction<TTarget> AsLuminAction<TTarget>(this Action action)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
#endif
        => LuminAction<TTarget>.Create(action);

    public static LuminAction<TTarget, T1> AsLuminAction<TTarget, T1>(this Action<T1> action)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
#endif
        => LuminAction<TTarget, T1>.Create(action);

    public static LuminAction<TTarget, T1, T2> AsLuminAction<TTarget, T1, T2>(this Action<T1, T2> action)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
#endif
        => LuminAction<TTarget, T1, T2>.Create(action);

    public static LuminAction<TTarget, T1, T2, T3> AsLuminAction<TTarget, T1, T2, T3>(this Action<T1, T2, T3> action)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
#endif
        => LuminAction<TTarget, T1, T2, T3>.Create(action);

    public static LuminAction<TTarget, T1, T2, T3, T4> AsLuminAction<TTarget, T1, T2, T3, T4>(this Action<T1, T2, T3, T4> action)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
#endif
        => LuminAction<TTarget, T1, T2, T3, T4>.Create(action);

    public static LuminAction<TTarget, T1, T2, T3, T4, T5> AsLuminAction<TTarget, T1, T2, T3, T4, T5>(this Action<T1, T2, T3, T4, T5> action)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
#endif
        => LuminAction<TTarget, T1, T2, T3, T4, T5>.Create(action);

    public static LuminAction<TTarget, T1, T2, T3, T4, T5, T6> AsLuminAction<TTarget, T1, T2, T3, T4, T5, T6>(this Action<T1, T2, T3, T4, T5, T6> action)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
#endif
        => LuminAction<TTarget, T1, T2, T3, T4, T5, T6>.Create(action);

    public static LuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7> AsLuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7>(this Action<T1, T2, T3, T4, T5, T6, T7> action)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
#endif
        => LuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7>.Create(action);

    public static LuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8> AsLuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8>(this Action<T1, T2, T3, T4, T5, T6, T7, T8> action)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
#endif
        => LuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8>.Create(action);

    public static LuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9> AsLuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9>(this Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> action)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
#endif
        => LuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9>.Create(action);

    public static LuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> AsLuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(this Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> action)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
#endif
        => LuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.Create(action);

    public static LuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> AsLuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(this Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> action)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
#endif
        => LuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>.Create(action);

    public static LuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> AsLuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(this Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> action)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
#endif
        => LuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.Create(action);

    public static LuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> AsLuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> action) 
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
#endif
        => LuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>.Create(action);
    
    public static LuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> AsLuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> action) 
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
        where T14 : allows ref struct
#endif
        => LuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>.Create(action);
    
    
    public static LuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> AsLuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15> action) 
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
        where T14 : allows ref struct
        where T15 : allows ref struct
#endif
        => LuminAction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>.Create(action);
    
    // Func 类型的扩展方法
    public static LuminFunction<TTarget, TResult> AsLuminFunction<TTarget, TResult>(this Func<TResult> func)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
#endif
        => LuminFunction<TTarget, TResult>.Create(func);

    public static LuminFunction<TTarget, T1, TResult> AsLuminFunction<TTarget, T1, TResult>(this Func<T1, TResult> func)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
#endif
        => LuminFunction<TTarget, T1, TResult>.Create(func);

    public static LuminFunction<TTarget, T1, T2, TResult> AsLuminFunction<TTarget, T1, T2, TResult>(this Func<T1, T2, TResult> func)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
#endif
        => LuminFunction<TTarget, T1, T2, TResult>.Create(func);

    public static LuminFunction<TTarget, T1, T2, T3, TResult> AsLuminFunction<TTarget, T1, T2, T3, TResult>(this Func<T1, T2, T3, TResult> func)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
#endif
        => LuminFunction<TTarget, T1, T2, T3, TResult>.Create(func);

    public static LuminFunction<TTarget, T1, T2, T3, T4, TResult> AsLuminFunction<TTarget, T1, T2, T3, T4, TResult>(this Func<T1, T2, T3, T4, TResult> func)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
#endif
        => LuminFunction<TTarget, T1, T2, T3, T4, TResult>.Create(func);

    public static LuminFunction<TTarget, T1, T2, T3, T4, T5, TResult> AsLuminFunction<TTarget, T1, T2, T3, T4, T5, TResult>(this Func<T1, T2, T3, T4, T5, TResult> func)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
#endif
        => LuminFunction<TTarget, T1, T2, T3, T4, T5, TResult>.Create(func);

    public static LuminFunction<TTarget, T1, T2, T3, T4, T5, T6, TResult> AsLuminFunction<TTarget, T1, T2, T3, T4, T5, T6, TResult>(this Func<T1, T2, T3, T4, T5, T6, TResult> func)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
#endif
        => LuminFunction<TTarget, T1, T2, T3, T4, T5, T6, TResult>.Create(func);

    public static LuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, TResult> AsLuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, TResult>(this Func<T1, T2, T3, T4, T5, T6, T7, TResult> func)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
#endif
        => LuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, TResult>.Create(func);

    public static LuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, TResult> AsLuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, TResult>(this Func<T1, T2, T3, T4, T5, T6, T7, T8, TResult> func)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
#endif
        => LuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, TResult>.Create(func);

    public static LuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult> AsLuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult>(this Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult> func)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
#endif
        => LuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, TResult>.Create(func);

    public static LuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult> AsLuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult>(this Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult> func)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
#endif
        => LuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TResult>.Create(func);

    public static LuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult> AsLuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult>(this Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult> func)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
#endif
        => LuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TResult>.Create(func);

    public static LuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult> AsLuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult>(this Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult> func)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
#endif
        => LuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TResult>.Create(func);

    public static LuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult> AsLuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult>(this Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult> func)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
#endif
        => LuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TResult>.Create(func);

    public static LuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult> AsLuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult>(this Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult> func)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
        where T14 : allows ref struct
#endif
        => LuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TResult>.Create(func);

    public static LuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult> AsLuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult>(this Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult> func)
#if NET9_0_OR_GREATER
        where TTarget : allows ref struct
        where T1 : allows ref struct
        where T2 : allows ref struct
        where T3 : allows ref struct
        where T4 : allows ref struct
        where T5 : allows ref struct
        where T6 : allows ref struct
        where T7 : allows ref struct
        where T8 : allows ref struct
        where T9 : allows ref struct
        where T10 : allows ref struct
        where T11 : allows ref struct
        where T12 : allows ref struct
        where T13 : allows ref struct
        where T14 : allows ref struct
        where T15 : allows ref struct
#endif
        => LuminFunction<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TResult>.Create(func);
    
    #endregion
    
    [StructLayout(LayoutKind.Auto)]
    public readonly struct MethodCache
    {
        internal readonly bool IsStatic;
        internal readonly IntPtr MethodPtr;
        internal readonly MethodInfo MethodBase;

        internal MethodCache(bool isStatic, IntPtr methodPtr, MethodInfo methodBase)
        {
            IsStatic = isStatic;
            MethodPtr = methodPtr;
            MethodBase = methodBase;
        }
    }
    
}