using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

using static LuminDelegates.LuminDelegate;

namespace LuminDelegates;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct LuminSpanAction<
#if NET8_0_OR_GREATER
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
#endif
TTarget, T, TArg> : IDisposable, ICloneable, ISerializable, IEquatable<LuminSpanAction<TTarget, T, TArg>>
#if NET9_0_OR_GREATER
    where TTarget : allows ref struct
#endif
{
    private void* _targetPointer; 
    private object? _referenceTarget; 
    private readonly IntPtr _methodPtr;
    private readonly bool _isStatic;
    private readonly MethodInfo _method; 
    private bool _disposed;

    private static readonly bool IsValueType = typeof(TTarget).IsValueType;
    
    public TTarget? Target
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ThrowIfDisposed();
            
            if (_isStatic)
                return default;
            
            if (IsValueType)
                return Unsafe.AsRef<TTarget?>(_targetPointer);
            
            var target = _referenceTarget;
            return Unsafe.As<object?, TTarget?>(ref target);
        }
    }
    
    public MethodInfo Method => _method;

    #region Constructors
    
    private LuminSpanAction(LuminDelegate.MethodCache method)
    {
        _method = method.MethodBase ?? throw new ArgumentNullException(nameof(method));
        _isStatic = method.IsStatic;
        _methodPtr = method.MethodPtr;


        if (!_isStatic)
            throw new ArgumentException("methods are not static!", method.MethodBase.Name);

        _referenceTarget = null;
        _targetPointer = null;
    }
    
    private LuminSpanAction(scoped ref TTarget? target, LuminDelegate.MethodCache method)
    {
        _method = method.MethodBase ?? throw new ArgumentNullException(nameof(method));
        _isStatic = method.IsStatic;
        _methodPtr = method.MethodPtr;
        _targetPointer = null;        
        if (!_isStatic)
        {
            if (IsValueType)
            {
#if NET8_0_OR_GREATER
                if (Unsafe.IsNullRef(ref target))
                    throw new ArgumentException("Target value instance cannot be null.", nameof(target));
#endif

#if NET8_0_OR_GREATER
                _targetPointer = NativeMemory.Alloc((nuint)Unsafe.SizeOf<TTarget>());
#else
                _targetPointer = Marshal.AllocHGlobal(Unsafe.SizeOf<TTarget>()).ToPointer();
#endif
                Unsafe.CopyBlock(
                    _targetPointer,
                    Unsafe.AsPointer(ref target),
                    (uint)Unsafe.SizeOf<TTarget>()
                );
            }
            else
            {
                // 引用类型：固定对象并获取指针
                if (target is null)
                    throw new ArgumentException("Target instance object is null.", nameof(target));
                
                _referenceTarget = Unsafe.As<TTarget, object?>(ref target);
                
            }
        }
        else
        {
            _referenceTarget = null;
        }
    }
    
    private LuminSpanAction(Delegate @delegate)
    {
        _method = @delegate.Method ?? throw new ArgumentNullException(nameof(@delegate.Method));
        _isStatic = @delegate.Method.IsStatic;
        _methodPtr = @delegate.Method.MethodHandle.GetFunctionPointer();
        _targetPointer = null;

        var target = @delegate.Target;
        if (!_isStatic)
        {
            if (target is not TTarget)
                throw new ArgumentException("Target instance object is not type.", typeof(TTarget).Name);
                
            if (IsValueType)
            {
#if NET8_0_OR_GREATER
                if (Unsafe.IsNullRef(ref target))
                    throw new ArgumentException("Target value instance cannot be null.", nameof(target));
#endif
                // 从Delegate创建，不可能是refLike
                var unboxedTarget = Unsafe.AsRef<TTarget>(Unsafe.AsPointer(ref target));
                
#if NET8_0_OR_GREATER
                _targetPointer = NativeMemory.Alloc((nuint)Unsafe.SizeOf<TTarget>());
#else
                _targetPointer = Marshal.AllocHGlobal(Unsafe.SizeOf<TTarget>()).ToPointer();
#endif
                Unsafe.CopyBlock(
                    _targetPointer,
                    Unsafe.AsPointer(ref unboxedTarget),
                    (uint)Unsafe.SizeOf<TTarget>()
                );
            }
            else
            {
                // 引用类型：固定对象并获取指针
                if (target is null)
                    throw new ArgumentException("Target instance object is null.", nameof(target));
                
                _referenceTarget = target;
            }
        }
        else
        {
            _referenceTarget = null;
        }
    }

    #endregion
    
    #region Create Factory

    /// <summary>
    /// 创建Lumin委托
    /// </summary>
    /// <param name="target">委托绑定的实例</param>
    /// <param name="methodName">委托绑定的方法名</param>
    /// <param name="methodNameHash">方法名哈希值（可选）</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static LuminSpanAction<TTarget, T, TArg> Create(scoped ref TTarget? target, string methodName, int methodNameHash = 0)
    {
        if (string.IsNullOrEmpty(methodName))
            throw new ArgumentException("Method name cannot be null or empty.", nameof(methodName));
        
        
        var method = GetCachedMethod(IsValueType
                ? TargetTypeHandle
                : Unsafe.As<TTarget, object?>(ref target!)?.GetType().TypeHandle ?? TargetTypeHandle,
            methodName, methodNameHash);
        
        if (method.MethodBase is null)
            throw new ArgumentException("Method does not exist.", nameof(methodName));
        
        return new LuminSpanAction<TTarget, T, TArg>(ref target, method);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static LuminSpanAction<TTarget, T, TArg> Create(scoped ref TTarget? target, string methodName, Type targetType, int methodNameHash = 0)
    {
        if (string.IsNullOrEmpty(methodName))
            throw new ArgumentException("Method name cannot be null or empty.", nameof(methodName));
        
        var method = GetCachedMethod(targetType.TypeHandle, methodName, methodNameHash);
        
        if (method.MethodBase is null)
            throw new ArgumentException("Method does not exist.", nameof(methodName));
        
        return new LuminSpanAction<TTarget, T, TArg>(ref target, method);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static LuminSpanAction<TTarget, T, TArg> Create(scoped ref TTarget? target, ReadOnlySpan<char> methodName, int methodNameHash = 0) =>
        Create(ref target, methodName.ToString(), methodNameHash);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static LuminSpanAction<TTarget, T, TArg> Create(scoped ref TTarget? target, ReadOnlySpan<char> methodName, Type targetType, int methodNameHash = 0) =>
        Create(ref target, methodName.ToString(), targetType, methodNameHash);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static LuminSpanAction<TTarget, T, TArg> Create(Delegate @delegate) => 
        new LuminSpanAction<TTarget, T, TArg>(@delegate);
    
    /// <summary>
    /// 创建绑定静态方法的Lumin委托
    /// </summary>
    /// <param name="methodName">委托绑定的方法名</param>
    /// <param name="methodNameHash">方法名哈希值（可选）</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static LuminSpanAction<TTarget, T, TArg> Create(string methodName, int methodNameHash = 0)
    {
        if (string.IsNullOrEmpty(methodName))
            throw new ArgumentException("Method name cannot be null or empty.", nameof(methodName));
        
        var method = GetCachedMethod(TargetTypeHandle, methodName, methodNameHash);
        
        if (method.MethodBase is null)
            throw new ArgumentException("Method does not exist.", nameof(methodName));
        
        if (!method.IsStatic)
            throw new ArgumentException("Method is not static, please pass in instance as parameter.", nameof(methodName));
        
        return new LuminSpanAction<TTarget, T, TArg>(method);
    }

    #endregion
    
    #region Interface Implementation

    public void Dispose()
    {
        if (_disposed)
            return;
        
        if (IsValueType && !_isStatic && _targetPointer != null)
        {
#if NET8_0_OR_GREATER
            NativeMemory.Free(_targetPointer);
#else
            Marshal.FreeHGlobal(new IntPtr(_targetPointer));
#endif
        }
        
        _targetPointer = null;
        _referenceTarget = null;

        _disposed = true;
    }
    
    public object Clone()
    {
        ThrowIfDisposed();
        var target = _referenceTarget;
        if (target is null)
            throw new NullReferenceException("Target instance cannot be null.");
                
        return new LuminSpanAction<TTarget, T, TArg>(ref Unsafe.As<object?, TTarget>(ref target)!, new LuminDelegate.MethodCache(_isStatic, _methodPtr, _method));
    }

    public void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        throw new NotSupportedException();
    }
    
    public override bool Equals(object? obj)
    {
        return obj is LuminSpanAction<TTarget, T, TArg> other && Equals(other);
    }

    public bool Equals(LuminSpanAction<TTarget, T, TArg> other)
    {
        ThrowIfDisposed();
        return _methodPtr == other._methodPtr &&
               _isStatic == other._isStatic &&
               _targetPointer == other._targetPointer;
    }

    public override int GetHashCode()
    {
        ThrowIfDisposed();
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + _methodPtr.GetHashCode();
            hash = hash * 31 + _isStatic.GetHashCode();
            hash = hash * 31 + ((IntPtr)_targetPointer).GetHashCode();
            return hash;
        }
    }

    #endregion
    
    #region Implicit
    
    public static implicit operator LuminSpanAction<TTarget, T, TArg>(in Delegate action) 
        => Create(action);

    // Action 类型的隐式转换
    public static implicit operator LuminSpanAction<TTarget, T, TArg>(SpanAction<T, TArg> action) => 
        Create(action);
    
    #endregion

    #region Invoke

    /// <summary>
    /// 使用反射动态调用方法
    /// 慎用，开销较大
    /// </summary>
    /// <param name="args">方法参数数组</param>
    /// <returns>方法返回值</returns>
    public object? DynamicInvoke(params object?[] args)
    {
        ThrowIfDisposed();
        
        if (_isStatic)
        {
            return _method.Invoke(null, args);
        }
        else
        {
            if (typeof(TTarget).IsByRefLike)
                throw new ArgumentException(
                    "The target type is a by-ref-like type and cannot be converted to a delegate.");

#if !NET9_0_OR_GREATER
            if (IsValueType)
                throw new ArgumentException("The target type is value type and cannot dynamic invoke.");
#endif

#if NET9_0_OR_GREATER
            object? instance = IsValueType
                ? RuntimeHelpers.Box(ref Unsafe.AsRef<byte>(_targetPointer), typeof(TTarget).TypeHandle)
                : _referenceTarget;
#else
            object? instance = _referenceTarget;
#endif

            if (instance is null)
                throw new InvalidOperationException("Target instance is null.");

            return _method.Invoke(instance, args);
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke(Span<T> span, TArg arg1)
    {
        ThrowIfDisposed();
 
        if (!_isStatic && !IsValueType)
            ((delegate* managed<TTarget, Span<T>, TArg, void>)_methodPtr)(Unsafe.As<object?, TTarget>(ref _referenceTarget), span, arg1);
        else if (IsValueType)
            ((delegate* managed<ref TTarget, Span<T>, TArg, void>)_methodPtr)(ref Unsafe.AsRef<TTarget>(_targetPointer), span, arg1);
        else
            ((delegate* managed<Span<T>, TArg, void>)_methodPtr)(span, arg1);
    }

    #endregion
    
    #region Auxiliary methods
    
    public TDelegate ToDelegate<TDelegate>()
        where TDelegate : Delegate
    {
        ThrowIfDisposed();
        if (_isStatic)
        {
            return (TDelegate)Delegate.CreateDelegate(typeof(TDelegate), _method);
        }
        else
        {
            if (typeof(TTarget).IsByRefLike)
                throw new ArgumentException("The target type is a by-ref-like type and cannot be converted to a delegate.");
            
#if !NET9_0_OR_GREATER
            if (IsValueType)
                throw new ArgumentException("The target type is value type and cannot be converted to a delegate.");
#endif

#if NET9_0_OR_GREATER
            object? instance = IsValueType 
                ? RuntimeHelpers.Box(ref Unsafe.AsRef<byte>(_targetPointer), typeof(TTarget).TypeHandle)
                : _referenceTarget;
#else
            object? instance = _referenceTarget;
#endif
            
            if (instance is null)
                throw new InvalidOperationException("Target instance is null.");
            
            return (TDelegate)Delegate.CreateDelegate(typeof(TDelegate), instance, _method);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LuminAction<TTarget, T, TArg>));
    }
    
    #endregion
    
    #region Static Type Hash

    private static readonly int TypeHash = ComputeTypeHash();
    private static readonly int TargetTypeHash = TargetTypeHandle.GetHashCode();
    private static readonly RuntimeTypeHandle TargetTypeHandle = typeof(TTarget).TypeHandle;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ComputeTypeHash()
    {
        unchecked
        {
            int h = (int)2166136261;
    
            h = (h * 16777619) ^ typeof(T).TypeHandle.GetHashCode();
            h = (h * 16777619) ^ typeof(TArg).TypeHandle.GetHashCode();
            return h;
        }
    }

    #endregion

    #region Method Caching

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static LuminDelegate.MethodCache GetCachedMethod(scoped in RuntimeTypeHandle type, string methodName, int nameHash = 0)
    {
        nameHash = nameHash is 0 ? StringComparer.Ordinal.GetHashCode(methodName) : nameHash;
        int key = type.Equals(TargetTypeHandle)
                      ? TypeHash ^ nameHash ^ TargetTypeHash
                      : TypeHash ^ nameHash ^ type.GetHashCode();

        if (!Methods.TryGetValue(key, out var result))
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static;
    
            Type[] paramTypes = [ typeof(Span<T>), typeof(TArg) ];

            var method = MethodFinder.GetConcreteMethod(typeof(TTarget), methodName, paramTypes);

            if (IsIl2Cpp)
                result = new MethodCache(method.IsStatic, method.MethodHandle.Value, method);
            else
                result = new MethodCache(method.IsStatic, method.MethodHandle.GetFunctionPointer(), method);

            Methods.TryAdd(key, result);
        }

        return result;
    }
    
    #endregion
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct LuminReadOnlySpanAction<
#if NET8_0_OR_GREATER
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
#endif
TTarget, T, TArg> : IDisposable, ICloneable, ISerializable, IEquatable<LuminReadOnlySpanAction<TTarget, T, TArg>>
#if NET9_0_OR_GREATER
    where TTarget : allows ref struct
#endif
{
    private void* _targetPointer; 
    private object? _referenceTarget; 
    private readonly IntPtr _methodPtr;
    private readonly bool _isStatic;
    private readonly MethodInfo _method; 
    private bool _disposed;

    private static readonly bool IsValueType = typeof(TTarget).IsValueType;
    
    public TTarget? Target
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ThrowIfDisposed();
            
            if (_isStatic)
                return default;
            
            if (IsValueType)
                return Unsafe.AsRef<TTarget?>(_targetPointer);
            
            var target = _referenceTarget;
            return Unsafe.As<object?, TTarget?>(ref target);
        }
    }
    
    public MethodInfo Method => _method;

    #region Constructors
    
    private LuminReadOnlySpanAction(LuminDelegate.MethodCache method)
    {
        _method = method.MethodBase ?? throw new ArgumentNullException(nameof(method));
        _isStatic = method.IsStatic;
        _methodPtr = method.MethodPtr;


        if (!_isStatic)
            throw new ArgumentException("methods are not static!", method.MethodBase.Name);

        _referenceTarget = null;
        _targetPointer = null;
    }
    
    private LuminReadOnlySpanAction(scoped ref TTarget? target, LuminDelegate.MethodCache method)
    {
        _method = method.MethodBase ?? throw new ArgumentNullException(nameof(method));
        _isStatic = method.IsStatic;
        _methodPtr = method.MethodPtr;
        _targetPointer = null;        
        if (!_isStatic)
        {
            if (IsValueType)
            {
#if NET8_0_OR_GREATER
                if (Unsafe.IsNullRef(ref target))
                    throw new ArgumentException("Target value instance cannot be null.", nameof(target));
#endif

#if NET8_0_OR_GREATER
                _targetPointer = NativeMemory.Alloc((nuint)Unsafe.SizeOf<TTarget>());
#else
                _targetPointer = Marshal.AllocHGlobal(Unsafe.SizeOf<TTarget>()).ToPointer();
#endif
                Unsafe.CopyBlock(
                    _targetPointer,
                    Unsafe.AsPointer(ref target),
                    (uint)Unsafe.SizeOf<TTarget>()
                );
            }
            else
            {
                // 引用类型：固定对象并获取指针
                if (target is null)
                    throw new ArgumentException("Target instance object is null.", nameof(target));
                
                _referenceTarget = Unsafe.As<TTarget, object?>(ref target);
                
            }
        }
        else
        {
            _referenceTarget = null;
        }
    }
    
    private LuminReadOnlySpanAction(Delegate @delegate)
    {
        _method = @delegate.Method ?? throw new ArgumentNullException(nameof(@delegate.Method));
        _isStatic = @delegate.Method.IsStatic;
        _methodPtr = @delegate.Method.MethodHandle.GetFunctionPointer();
        _targetPointer = null;

        var target = @delegate.Target;
        if (!_isStatic)
        {
            if (target is not TTarget)
                throw new ArgumentException("Target instance object is not type.", typeof(TTarget).Name);
                
            if (IsValueType)
            {
#if NET8_0_OR_GREATER
                if (Unsafe.IsNullRef(ref target))
                    throw new ArgumentException("Target value instance cannot be null.", nameof(target));
#endif
                // 从Delegate创建，不可能是refLike
                var unboxedTarget = Unsafe.AsRef<TTarget>(Unsafe.AsPointer(ref target));
                
#if NET8_0_OR_GREATER
                _targetPointer = NativeMemory.Alloc((nuint)Unsafe.SizeOf<TTarget>());
#else
                _targetPointer = Marshal.AllocHGlobal(Unsafe.SizeOf<TTarget>()).ToPointer();
#endif
                Unsafe.CopyBlock(
                    _targetPointer,
                    Unsafe.AsPointer(ref unboxedTarget),
                    (uint)Unsafe.SizeOf<TTarget>()
                );
            }
            else
            {
                // 引用类型：固定对象并获取指针
                if (target is null)
                    throw new ArgumentException("Target instance object is null.", nameof(target));
                
                _referenceTarget = target;
            }
        }
        else
        {
            _referenceTarget = null;
        }
    }

    #endregion
    
    #region Create Factory

    /// <summary>
    /// 创建Lumin委托
    /// </summary>
    /// <param name="target">委托绑定的实例</param>
    /// <param name="methodName">委托绑定的方法名</param>
    /// <param name="methodNameHash">方法名哈希值（可选）</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static LuminReadOnlySpanAction<TTarget, T, TArg> Create(scoped ref TTarget? target, string methodName, int methodNameHash = 0)
    {
        if (string.IsNullOrEmpty(methodName))
            throw new ArgumentException("Method name cannot be null or empty.", nameof(methodName));
        
        
        var method = GetCachedMethod(IsValueType
                ? TargetTypeHandle
                : Unsafe.As<TTarget, object?>(ref target!)?.GetType().TypeHandle ?? TargetTypeHandle,
            methodName, methodNameHash);
        
        if (method.MethodBase is null)
            throw new ArgumentException("Method does not exist.", nameof(methodName));
        
        return new LuminReadOnlySpanAction<TTarget, T, TArg>(ref target, method);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static LuminReadOnlySpanAction<TTarget, T, TArg> Create(scoped ref TTarget? target, string methodName, Type targetType, int methodNameHash = 0)
    {
        if (string.IsNullOrEmpty(methodName))
            throw new ArgumentException("Method name cannot be null or empty.", nameof(methodName));
        
        var method = GetCachedMethod(targetType.TypeHandle, methodName, methodNameHash);
        
        if (method.MethodBase is null)
            throw new ArgumentException("Method does not exist.", nameof(methodName));
        
        return new LuminReadOnlySpanAction<TTarget, T, TArg>(ref target, method);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static LuminReadOnlySpanAction<TTarget, T, TArg> Create(scoped ref TTarget? target, ReadOnlySpan<char> methodName, int methodNameHash = 0) =>
        Create(ref target, methodName.ToString(), methodNameHash);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static LuminReadOnlySpanAction<TTarget, T, TArg> Create(scoped ref TTarget? target, ReadOnlySpan<char> methodName, Type targetType, int methodNameHash = 0) =>
        Create(ref target, methodName.ToString(), targetType, methodNameHash);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static LuminReadOnlySpanAction<TTarget, T, TArg> Create(Delegate @delegate) => 
        new LuminReadOnlySpanAction<TTarget, T, TArg>(@delegate);
    
    /// <summary>
    /// 创建绑定静态方法的Lumin委托
    /// </summary>
    /// <param name="methodName">委托绑定的方法名</param>
    /// <param name="methodNameHash">方法名哈希值（可选）</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static LuminReadOnlySpanAction<TTarget, T, TArg> Create(string methodName, int methodNameHash = 0)
    {
        if (string.IsNullOrEmpty(methodName))
            throw new ArgumentException("Method name cannot be null or empty.", nameof(methodName));
        
        var method = GetCachedMethod(TargetTypeHandle, methodName, methodNameHash);
        
        if (method.MethodBase is null)
            throw new ArgumentException("Method does not exist.", nameof(methodName));
        
        if (!method.IsStatic)
            throw new ArgumentException("Method is not static, please pass in instance as parameter.", nameof(methodName));
        
        return new LuminReadOnlySpanAction<TTarget, T, TArg>(method);
    }

    #endregion
    
    #region Interface Implementation

    public void Dispose()
    {
        if (_disposed)
            return;
        
        if (IsValueType && !_isStatic && _targetPointer != null)
        {
#if NET8_0_OR_GREATER
            NativeMemory.Free(_targetPointer);
#else
            Marshal.FreeHGlobal(new IntPtr(_targetPointer));
#endif
        }
        
        _targetPointer = null;
        _referenceTarget = null;

        _disposed = true;
    }
    
    public object Clone()
    {
        ThrowIfDisposed();
        var target = _referenceTarget;
        if (target is null)
            throw new NullReferenceException("Target instance cannot be null.");
                
        return new LuminReadOnlySpanAction<TTarget, T, TArg>(ref Unsafe.As<object?, TTarget>(ref target)!, new LuminDelegate.MethodCache(_isStatic, _methodPtr, _method));
    }

    public void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        throw new NotSupportedException();
    }
    
    public override bool Equals(object? obj)
    {
        return obj is LuminReadOnlySpanAction<TTarget, T, TArg> other && Equals(other);
    }

    public bool Equals(LuminReadOnlySpanAction<TTarget, T, TArg> other)
    {
        ThrowIfDisposed();
        return _methodPtr == other._methodPtr &&
               _isStatic == other._isStatic &&
               _targetPointer == other._targetPointer;
    }

    public override int GetHashCode()
    {
        ThrowIfDisposed();
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + _methodPtr.GetHashCode();
            hash = hash * 31 + _isStatic.GetHashCode();
            hash = hash * 31 + ((IntPtr)_targetPointer).GetHashCode();
            return hash;
        }
    }

    #endregion
    
    #region Implicit
    
    public static implicit operator LuminReadOnlySpanAction<TTarget, T, TArg>(in Delegate action) 
        => Create(action);

    // Action 类型的隐式转换
    public static implicit operator LuminReadOnlySpanAction<TTarget, T, TArg>(ReadOnlySpanAction<T, TArg> action) => 
        Create(action);
    
    #endregion

    #region Invoke

    /// <summary>
    /// 使用反射动态调用方法
    /// 慎用，开销较大
    /// </summary>
    /// <param name="args">方法参数数组</param>
    /// <returns>方法返回值</returns>
    public object? DynamicInvoke(params object?[] args)
    {
        ThrowIfDisposed();
        
        if (_isStatic)
        {
            return _method.Invoke(null, args);
        }
        else
        {
            if (typeof(TTarget).IsByRefLike)
                throw new ArgumentException(
                    "The target type is a by-ref-like type and cannot be converted to a delegate.");

#if !NET9_0_OR_GREATER
            if (IsValueType)
                throw new ArgumentException("The target type is value type and cannot dynamic invoke.");
#endif

#if NET9_0_OR_GREATER
            object? instance = IsValueType
                ? RuntimeHelpers.Box(ref Unsafe.AsRef<byte>(_targetPointer), typeof(TTarget).TypeHandle)
                : _referenceTarget;
#else
            object? instance = _referenceTarget;
#endif

            if (instance is null)
                throw new InvalidOperationException("Target instance is null.");

            return _method.Invoke(instance, args);
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke(ReadOnlySpan<T> span, TArg arg1)
    {
        ThrowIfDisposed();
 
        if (!_isStatic && !IsValueType)
            ((delegate* managed<TTarget, ReadOnlySpan<T>, TArg, void>)_methodPtr)(Unsafe.As<object?, TTarget>(ref _referenceTarget), span, arg1);
        else if (IsValueType)
            ((delegate* managed<ref TTarget, ReadOnlySpan<T>, TArg, void>)_methodPtr)(ref Unsafe.AsRef<TTarget>(_targetPointer), span, arg1);
        else
            ((delegate* managed<ReadOnlySpan<T>, TArg, void>)_methodPtr)(span, arg1);
    }

    #endregion
    
    #region Auxiliary methods
    
    public TDelegate ToDelegate<TDelegate>()
        where TDelegate : Delegate
    {
        ThrowIfDisposed();
        if (_isStatic)
        {
            return (TDelegate)Delegate.CreateDelegate(typeof(TDelegate), _method);
        }
        else
        {
            if (typeof(TTarget).IsByRefLike)
                throw new ArgumentException("The target type is a by-ref-like type and cannot be converted to a delegate.");
            
#if !NET9_0_OR_GREATER
            if (IsValueType)
                throw new ArgumentException("The target type is value type and cannot be converted to a delegate.");
#endif

#if NET9_0_OR_GREATER
            object? instance = IsValueType 
                ? RuntimeHelpers.Box(ref Unsafe.AsRef<byte>(_targetPointer), typeof(TTarget).TypeHandle)
                : _referenceTarget;
#else
            object? instance = _referenceTarget;
#endif
            
            if (instance is null)
                throw new InvalidOperationException("Target instance is null.");
            
            return (TDelegate)Delegate.CreateDelegate(typeof(TDelegate), instance, _method);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LuminAction<TTarget, T, TArg>));
    }
    
    #endregion
    
    #region Static Type Hash

    private static readonly int TypeHash = ComputeTypeHash();
    private static readonly int TargetTypeHash = TargetTypeHandle.GetHashCode();
    private static readonly RuntimeTypeHandle TargetTypeHandle = typeof(TTarget).TypeHandle;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ComputeTypeHash()
    {
        unchecked
        {
            int h = (int)2166136261;
    
            h = (h * 16777619) ^ typeof(T).TypeHandle.GetHashCode();
            h = (h * 16777619) ^ typeof(TArg).TypeHandle.GetHashCode();
            return h;
        }
    }

    #endregion

    #region Method Caching

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static LuminDelegate.MethodCache GetCachedMethod(scoped in RuntimeTypeHandle type, string methodName, int nameHash = 0)
    {
        nameHash = nameHash is 0 ? StringComparer.Ordinal.GetHashCode(methodName) : nameHash;
        int key = type.Equals(TargetTypeHandle)
                      ? TypeHash ^ nameHash ^ TargetTypeHash
                      : TypeHash ^ nameHash ^ type.GetHashCode();

        if (!Methods.TryGetValue(key, out var result))
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static;
    
            Type[] paramTypes = [ typeof(ReadOnlySpan<T>), typeof(TArg) ];

            var method = MethodFinder.GetConcreteMethod(typeof(TTarget), methodName, paramTypes);

            if (IsIl2Cpp)
                result = new MethodCache(method.IsStatic, method.MethodHandle.Value, method);
            else
                result = new MethodCache(method.IsStatic, method.MethodHandle.GetFunctionPointer(), method);

            Methods.TryAdd(key, result);
        }

        return result;
    }
    
    #endregion
}