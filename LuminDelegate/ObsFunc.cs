using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace LuminDelegates;

/// <typeparam name="TTarget">实例Type</typeparam>
[StructLayout(LayoutKind.Auto)]
public unsafe struct LuminFunction< 
#if NET8_0_OR_GREATER
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
#endif
    TTarget> : IDisposable, ICloneable, ISerializable, IEquatable<LuminFunction<TTarget>>
#if NET9_0_OR_GREATER
    where TTarget : allows ref struct
#endif
{
    private readonly void* _targetPointer; // 统一的目标指针（值类型或引用类型）
    private readonly IntPtr _methodPtr;
    private readonly bool _isStatic;
    private GCHandle _handle; // 用于固定引用类型对象
    private readonly MethodInfo _method; // 存储关联的方法信息
    private bool _disposed;
    
    private static readonly bool IsValueType = typeof(TTarget).IsValueType;
    private static readonly ConcurrentDictionary<(RuntimeTypeHandle, string), (MethodInfo, MethodCache)> Methods = new();
    
    
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
            
            var target = _handle.Target;
            return Unsafe.As<object?, TTarget?>(ref target);
        }
    }
    
    public MethodInfo Method => _method;

    #region Constructors
    
    private LuminFunction((MethodInfo, MethodCache) method)
    {
        _method = method.Item1 ?? throw new ArgumentNullException(nameof(method));
        _isStatic = method.Item2.IsStatic;
        _methodPtr = method.Item2.MethodPtr;
        _handle = default;

        if (!_isStatic)
            throw new ArgumentException("methods are not static!", method.Item1.Name);
        
        _targetPointer = null;
    }
    
    private LuminFunction(scoped ref TTarget? target, (MethodInfo, MethodCache) method)
    {
        _method = method.Item1 ?? throw new ArgumentNullException(nameof(method));
        _isStatic = method.Item2.IsStatic;
        _methodPtr = method.Item2.MethodPtr;
        _handle = default;
        
        if (!_isStatic)
        {
            if (IsValueType)
            {
                // 值类型：直接获取指针
                if (Unsafe.IsNullRef(ref target))
                    throw new ArgumentException("Target value instance cannot be null.", nameof(target));
                
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
                
                object? obj = Unsafe.As<TTarget, object?>(ref target);
                
                _handle = GCHandle.Alloc(obj, GCHandleType.Pinned);
                _targetPointer = (void*)_handle.AddrOfPinnedObject();
            }
        }
        else
        {
            _targetPointer = null;
        }
    }
    
    private LuminFunction(Delegate @delegate)
    {
        _method = @delegate.Method ?? throw new ArgumentNullException(nameof(@delegate.Method));
        _isStatic = @delegate.Method.IsStatic;
        _methodPtr = @delegate.Method.MethodHandle.GetFunctionPointer();
        _handle = default;
        
        var target = @delegate.Target;
        if (!_isStatic)
        {
            if (target is not TTarget)
                throw new ArgumentException("Target instance object is not type.", typeof(TTarget).Name);
                
            if (IsValueType)
            {
                // 值类型：直接获取指针
                if (Unsafe.IsNullRef(ref target))
                    throw new ArgumentException("Target value instance cannot be null.", nameof(target));
                
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
                
                _handle = GCHandle.Alloc(target, GCHandleType.Pinned);
                _targetPointer = (void*)_handle.AddrOfPinnedObject();
            }
        }
        else
        {
            _targetPointer = null;
        }
    }

    #endregion

    #region Create Factory

    /// <summary>
    /// 创建Lumin委托
    /// </summary>
    /// <param name="target">委托绑定的实例</param>
    /// <param name="methodName">委托绑定的方法名</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static LuminFunction<TTarget> Create(scoped ref TTarget? target, string methodName)
    {
        if (string.IsNullOrEmpty(methodName))
            throw new ArgumentException("Method name cannot be null or empty.", nameof(methodName));
        
        var method = GetCachedMethod(IsValueType ? typeof(TTarget).TypeHandle : Unsafe.As<TTarget, object?>(ref target!)?.GetType().TypeHandle ?? typeof(TTarget).TypeHandle, methodName);
        
        if (method.Item1 is null)
            throw new ArgumentException("Method does not exist.", nameof(methodName));
        
        return new LuminFunction<TTarget>(ref target, method);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static LuminFunction<TTarget> Create(scoped ref TTarget? target, string methodName, Type targetType)
    {
        if (string.IsNullOrEmpty(methodName))
            throw new ArgumentException("Method name cannot be null or empty.", nameof(methodName));
        
        var method = GetCachedMethod(targetType.TypeHandle, methodName);
        
        if (method.Item1 is null)
            throw new ArgumentException("Method does not exist.", nameof(methodName));
        
        return new LuminFunction<TTarget>(ref target, method);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static LuminFunction<TTarget> Create(scoped ref TTarget? target, ReadOnlySpan<char> methodName) =>
        Create(ref target, methodName.ToString());
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static LuminFunction<TTarget> Create(scoped ref TTarget? target, ReadOnlySpan<char> methodName, Type targetType) =>
        Create(ref target, methodName.ToString(), targetType);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static LuminFunction<TTarget> Create(Delegate @delegate) => 
        new LuminFunction<TTarget>(@delegate);
    
    /// <summary>
    /// 创建绑定静态方法的Lumin委托
    /// </summary>
    /// <param name="methodName">委托绑定的方法名</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static LuminFunction<TTarget> Create(string methodName)
    {
        if (string.IsNullOrEmpty(methodName))
            throw new ArgumentException("Method name cannot be null or empty.", nameof(methodName));
        
        var method = GetCachedMethod(typeof(TTarget).TypeHandle, methodName);
        
        if (method.Item1 is null)
            throw new ArgumentException("Method does not exist.", nameof(methodName));
        
        if (!method.Item2.IsStatic)
            throw new ArgumentException("Method is not static, please pass in instance as parameter.", nameof(methodName));
        
        return new LuminFunction<TTarget>(method);
    }

    #endregion

    #region Interface Implementation

    public void Dispose()
    {
        if (_disposed)
            return;
        
        if (_handle.IsAllocated)
        {
            _handle.Free();
        }
        
        if (IsValueType && !_isStatic && _targetPointer != null)
        {
#if NET8_0_OR_GREATER
            NativeMemory.Free(_targetPointer);
#else
            Marshal.FreeHGlobal(new IntPtr(_targetPointer));
#endif
        }
        
        _disposed = true;
    }
    
    public object Clone()
    {
        ThrowIfDisposed();
        var target = _handle.Target;
        if (target is null)
            throw new NullReferenceException("Target instance cannot be null.");
                
        return new LuminFunction<TTarget>(ref Unsafe.As<object?, TTarget>(ref target)!, (_method, new MethodCache(_isStatic, _methodPtr)));
    }

    public void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        throw new NotSupportedException();
    }
    
    public override bool Equals(object? obj)
    {
        return obj is LuminFunction<TTarget> other && Equals(other);
    }

    public bool Equals(LuminFunction<TTarget> other)
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
    
    public static implicit operator LuminFunction<TTarget>(in Delegate action) 
        => Create(action);

    // Action 类型的隐式转换
    public static implicit operator LuminFunction<TTarget>(Action action) => Create(action);

    public static implicit operator LuminFunction<TTarget>(Action<object> action) => Create(action);

    public static implicit operator LuminFunction<TTarget>(Action<object, object> action) => Create(action);

    public static implicit operator LuminFunction<TTarget>(Action<object, object, object> action) => Create(action);

    public static implicit operator LuminFunction<TTarget>(Action<object, object, object, object> action) => Create(action);

    public static implicit operator LuminFunction<TTarget>(Action<object, object, object, object, object> action) => Create(action);

    public static implicit operator LuminFunction<TTarget>(Action<object, object, object, object, object, object> action) => Create(action);

    public static implicit operator LuminFunction<TTarget>(Action<object, object, object, object, object, object, object> action) => Create(action);

    public static implicit operator LuminFunction<TTarget>(Action<object, object, object, object, object, object, object, object> action) => Create(action);

    public static implicit operator LuminFunction<TTarget>(Action<object, object, object, object, object, object, object, object, object> action) => Create(action);

    public static implicit operator LuminFunction<TTarget>(Action<object, object, object, object, object, object, object, object, object, object> action) => Create(action);

    public static implicit operator LuminFunction<TTarget>(Action<object, object, object, object, object, object, object, object, object, object, object> action) => Create(action);

    public static implicit operator LuminFunction<TTarget>(Action<object, object, object, object, object, object, object, object, object, object, object, object> action) => Create(action);

    public static implicit operator LuminFunction<TTarget>(Action<object, object, object, object, object, object, object, object, object, object, object, object, object> action) => Create(action);

    public static implicit operator LuminFunction<TTarget>(Action<object, object, object, object, object, object, object, object, object, object, object, object, object, object> action) => Create(action);

    public static implicit operator LuminFunction<TTarget>(Action<object, object, object, object, object, object, object, object, object, object, object, object, object, object, object> action) => Create(action);

    // Func 类型的隐式转换
    public static implicit operator LuminFunction<TTarget>(Func<object> func) => Create(func);

    public static implicit operator LuminFunction<TTarget>(Func<object, object> func) => Create(func);

    public static implicit operator LuminFunction<TTarget>(Func<object, object, object> func) => Create(func);

    public static implicit operator LuminFunction<TTarget>(Func<object, object, object, object> func) => Create(func);

    public static implicit operator LuminFunction<TTarget>(Func<object, object, object, object, object> func) => Create(func);

    public static implicit operator LuminFunction<TTarget>(Func<object, object, object, object, object, object> func) => Create(func);

    public static implicit operator LuminFunction<TTarget>(Func<object, object, object, object, object, object, object> func) => Create(func);

    public static implicit operator LuminFunction<TTarget>(Func<object, object, object, object, object, object, object, object> func) => Create(func);

    public static implicit operator LuminFunction<TTarget>(Func<object, object, object, object, object, object, object, object, object> func) => Create(func);

    public static implicit operator LuminFunction<TTarget>(Func<object, object, object, object, object, object, object, object, object, object> func) => Create(func);

    public static implicit operator LuminFunction<TTarget>(Func<object, object, object, object, object, object, object, object, object, object, object> func) => Create(func);

    public static implicit operator LuminFunction<TTarget>(Func<object, object, object, object, object, object, object, object, object, object, object, object> func) => Create(func);
    
    public static implicit operator LuminFunction<TTarget>(Func<object, object, object, object, object, object, object, object, object, object, object, object, object> func) => Create(func);
    
    public static implicit operator LuminFunction<TTarget>(Func<object, object, object, object, object, object, object, object, object, object, object, object, object, object> func) => Create(func);
    
    public static implicit operator LuminFunction<TTarget>(Func<object, object, object, object, object, object, object, object, object, object, object, object, object, object, object> func) => Create(func);
    
    public static implicit operator LuminFunction<TTarget>(Func<object, object, object, object, object, object, object, object, object, object, object, object, object, object, object, object> func) => Create(func);
    
    
    #endregion
    
    #region Invoke Methods
    
    
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
                throw new ArgumentException("The target type is value type and cannot be converted to a delegate.");
#endif

#if NET9_0_OR_GREATER
            object? instance = IsValueType
                ? RuntimeHelpers.Box(ref Unsafe.AsRef<byte>(_targetPointer), typeof(TTarget).TypeHandle)
                : _handle.Target;
#else
            object? instance = _handle.Target;
#endif

            if (instance is null)
                throw new InvalidOperationException("Target instance is null.");

            return _method.Invoke(instance, args);
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke(bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch)
            ThrowIfSignatureMismatch(typeof(void));

        if (_isStatic)
        {
            var func = (delegate* managed<void>)_methodPtr;
            func();
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, void>)_methodPtr;
            func(ref Unsafe.AsRef<TTarget>(_targetPointer));
        }
        else
        {
            var func = (delegate* managed<TTarget, void>)_methodPtr;
            var obj = _handle.Target;
            func(Unsafe.As<object?, TTarget>(ref obj));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TReturn Invoke<TReturn>(bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch(typeof(TReturn));
        
        if (_isStatic)
        {
            var func = (delegate* managed<TReturn>)_methodPtr;
            return func();
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, TReturn>)_methodPtr;
            return func(ref Unsafe.AsRef<TTarget>(_targetPointer));
        }
        else
        {
            var func = (delegate* managed<TTarget, TReturn>)_methodPtr;
            var obj = _handle.Target;
            return func(Unsafe.As<object?, TTarget>(ref obj));
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<TReturn>(out TReturn returnValue, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        returnValue = Invoke<TReturn>(checkSignatureMismatch);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1>(T1 arg1, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1>(typeof(void));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, void>)_methodPtr;
            func(arg1);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, void>)_methodPtr;
            func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, void>)_methodPtr;
            var obj = _handle.Target;
            func(Unsafe.As<object?, TTarget>(ref obj), arg1);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TReturn Invoke<T1, TReturn>(T1 arg1, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1>(typeof(TReturn));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, TReturn>)_methodPtr;
            return func(arg1);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, TReturn>)_methodPtr;
            return func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, TReturn>)_methodPtr;
            var obj = _handle.Target;
            return func(Unsafe.As<object?, TTarget>(ref obj), arg1);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, TReturn>(T1 arg1, out TReturn returnValue, bool checkSignatureMismatch = false)
    {
        returnValue = Invoke<T1, TReturn>(arg1, checkSignatureMismatch);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2>(T1 arg1, T2 arg2, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2>(typeof(void));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, void>)_methodPtr;
            func(arg1, arg2);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, void>)_methodPtr;
            func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, void>)_methodPtr;
            var obj = _handle.Target;
            func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TReturn Invoke<T1, T2, TReturn>(T1 arg1, T2 arg2, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2>(typeof(TReturn));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, TReturn>)_methodPtr;
            return func(arg1, arg2);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, TReturn>)_methodPtr;
            return func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, TReturn>)_methodPtr;
            var obj = _handle.Target;
            return func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2);
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, TReturn>(T1 arg1, T2 arg2, out TReturn returnValue, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        returnValue = Invoke<T1, T2, TReturn>(arg1, arg2, checkSignatureMismatch);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, T3>(T1 arg1, T2 arg2, T3 arg3, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2, T3>(typeof(void));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, T3, void>)_methodPtr;
            func(arg1, arg2, arg3);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, T3, void>)_methodPtr;
            func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2, arg3);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, T3, void>)_methodPtr;
            var obj = _handle.Target;
            func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2, arg3);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TReturn Invoke<T1, T2, T3, TReturn>(T1 arg1, T2 arg2, T3 arg3, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2, T3>(typeof(TReturn));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, T3, TReturn>)_methodPtr;
            return func(arg1, arg2, arg3);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, T3, TReturn>)_methodPtr;
            return func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2, arg3);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, T3, TReturn>)_methodPtr;
            var obj = _handle.Target;
            return func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2, arg3);
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, T3, TReturn>(T1 arg1, T2 arg2, T3 arg3, out TReturn returnValue, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        returnValue = Invoke<T1, T2, T3, TReturn>(arg1, arg2, arg3, checkSignatureMismatch);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, T3, T4>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2, T3, T4>(typeof(void));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, T3, T4, void>)_methodPtr;
            func(arg1, arg2, arg3, arg4);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, T3, T4, void>)_methodPtr;
            func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2, arg3, arg4);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, T3, T4, void>)_methodPtr;
            var obj = _handle.Target;
            func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2, arg3, arg4);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TReturn Invoke<T1, T2, T3, T4, TReturn>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2, T3, T4>(typeof(TReturn));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, T3, T4, TReturn>)_methodPtr;
            return func(arg1, arg2, arg3, arg4);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, T3, T4, TReturn>)_methodPtr;
            return func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2, arg3, arg4);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, T3, T4, TReturn>)_methodPtr;
            var obj = _handle.Target;
            return func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2, arg3, arg4);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, T3, T4, TReturn>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, out TReturn returnValue, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        returnValue = Invoke<T1, T2, T3, T4, TReturn>(arg1, arg2, arg3, arg4, checkSignatureMismatch);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, T3, T4, T5>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2, T3, T4, T5>(typeof(void));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, T3, T4, T5, void>)_methodPtr;
            func(arg1, arg2, arg3, arg4, arg5);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, T3, T4, T5, void>)_methodPtr;
            func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2, arg3, arg4, arg5);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, T3, T4, T5, void>)_methodPtr;
            var obj = _handle.Target;
            func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2, arg3, arg4, arg5);
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TReturn Invoke<T1, T2, T3, T4, T5, TReturn>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2, T3, T4, T5>(typeof(TReturn));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, T3, T4, T5, TReturn>)_methodPtr;
            return func(arg1, arg2, arg3, arg4, arg5);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, T3, T4, T5, TReturn>)_methodPtr;
            return func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2, arg3, arg4, arg5);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, T3, T4, T5, TReturn>)_methodPtr;
            var obj = _handle.Target;
            return func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2, arg3, arg4, arg5);
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, T3, T4, T5, TReturn>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, out TReturn returnValue, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        returnValue = Invoke<T1, T2, T3, T4, T5, TReturn>(arg1, arg2, arg3, arg4, arg5, checkSignatureMismatch);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, T3, T4, T5, T6>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch)
            ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6>(typeof(void));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, T3, T4, T5, T6, void>)_methodPtr;
            func(arg1, arg2, arg3, arg4, arg5, arg6);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, T3, T4, T5, T6, void>)_methodPtr;
            func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2, arg3, arg4, arg5, arg6);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, T3, T4, T5, T6, void>)_methodPtr;
            var obj = _handle.Target;
            func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2, arg3, arg4, arg5, arg6);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TReturn Invoke<T1, T2, T3, T4, T5, T6, TReturn>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6>(typeof(TReturn));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, T3, T4, T5, T6, TReturn>)_methodPtr;
            return func(arg1, arg2, arg3, arg4, arg5, arg6);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, T3, T4, T5, T6, TReturn>)_methodPtr;
            return func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2, arg3, arg4, arg5, arg6);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, T3, T4, T5, T6, TReturn>)_methodPtr;
            var obj = _handle.Target;
            return func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2, arg3, arg4, arg5, arg6);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, T3, T4, T5, T6, TReturn>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, out TReturn returnValue, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        returnValue = Invoke<T1, T2, T3, T4, T5, T6, TReturn>(arg1, arg2, arg3, arg4, arg5, arg6, checkSignatureMismatch);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, T3, T4, T5, T6, T7>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7>(typeof(void));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, T3, T4, T5, T6, T7, void>)_methodPtr;
            func(arg1, arg2, arg3, arg4, arg5, arg6, arg7);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, T3, T4, T5, T6, T7, void>)_methodPtr;
            func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2, arg3, arg4, arg5, arg6, arg7);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, T3, T4, T5, T6, T7, void>)_methodPtr;
            var obj = _handle.Target;
            func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2, arg3, arg4, arg5, arg6, arg7);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TReturn Invoke<T1, T2, T3, T4, T5, T6, T7, TReturn>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7>(typeof(TReturn));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, T3, T4, T5, T6, T7, TReturn>)_methodPtr;
            return func(arg1, arg2, arg3, arg4, arg5, arg6, arg7);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, T3, T4, T5, T6, T7, TReturn>)_methodPtr;
            return func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2, arg3, arg4, arg5, arg6, arg7);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, T3, T4, T5, T6, T7, TReturn>)_methodPtr;
            var obj = _handle.Target;
            return func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2, arg3, arg4, arg5, arg6, arg7);
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, T3, T4, T5, T6, T7, TReturn>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, out TReturn returnValue, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        returnValue = Invoke<T1, T2, T3, T4, T5, T6, T7, TReturn>(arg1, arg2, arg3, arg4, arg5, arg6, arg7, checkSignatureMismatch);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7, T8>(typeof(void));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, T3, T4, T5, T6, T7, T8, void>)_methodPtr;
            func(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, T3, T4, T5, T6, T7, T8, void>)_methodPtr;
            func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, void>)_methodPtr;
            var obj = _handle.Target;
            func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TReturn Invoke<T1, T2, T3, T4, T5, T6, T7, T8, TReturn>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7, T8>(typeof(TReturn));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, T3, T4, T5, T6, T7, T8, TReturn>)_methodPtr;
            return func(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, T3, T4, T5, T6, T7, T8, TReturn>)_methodPtr;
            return func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, TReturn>)_methodPtr;
            var obj = _handle.Target;
            return func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8, TReturn>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, out TReturn returnValue, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        returnValue = Invoke<T1, T2, T3, T4, T5, T6, T7, T8, TReturn>(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, checkSignatureMismatch);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7, T8, T9>(typeof(void));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, T3, T4, T5, T6, T7, T8, T9, void>)_methodPtr;
            func(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, void>)_methodPtr;
            func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, void>)_methodPtr;
            var obj = _handle.Target;
            func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TReturn Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, TReturn>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7, T8, T9>(typeof(TReturn));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, T3, T4, T5, T6, T7, T8, T9, TReturn>)_methodPtr;
            return func(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, TReturn>)_methodPtr;
            return func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, TReturn>)_methodPtr;
            var obj = _handle.Target;
            return func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9);
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, TReturn>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, out TReturn returnValue, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        returnValue = Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, TReturn>(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, checkSignatureMismatch);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(typeof(void));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, void>)_methodPtr;
            func(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, void>)_methodPtr;
            func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, void>)_methodPtr;
            var obj = _handle.Target;
            func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TReturn Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TReturn>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(typeof(TReturn));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TReturn>)_methodPtr;
            return func(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TReturn>)_methodPtr;
            return func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TReturn>)_methodPtr;
            var obj = _handle.Target;
            return func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TReturn>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, out TReturn returnValue, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        returnValue = Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, TReturn>(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, checkSignatureMismatch);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
        T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(typeof(void));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, void>)_methodPtr;
            func(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, void>)_methodPtr;
            func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, void>)_methodPtr;
            var obj = _handle.Target;
            func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TReturn Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TReturn>(
        T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(typeof(TReturn));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TReturn>)_methodPtr;
            return func(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TReturn>)_methodPtr;
            return func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TReturn>)_methodPtr;
            var obj = _handle.Target;
            return func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11);
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TReturn>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, out TReturn returnValue, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        returnValue = Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, TReturn>(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, checkSignatureMismatch);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
        T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(typeof(void));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, void>)_methodPtr;
            func(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, void>)_methodPtr;
            func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, void>)_methodPtr;
            var obj = _handle.Target;
            func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TReturn Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TReturn>(
        T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(typeof(TReturn));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TReturn>)_methodPtr;
            return func(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TReturn>)_methodPtr;
            return func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TReturn>)_methodPtr;
            var obj = _handle.Target;
            return func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TReturn>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, out TReturn returnValue, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        returnValue = Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, TReturn>(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, checkSignatureMismatch);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
        T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(typeof(void));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, void>)_methodPtr;
            func(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, void>)_methodPtr;
            func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, void>)_methodPtr;
            var obj = _handle.Target;
            func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TReturn Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TReturn>(
        T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(typeof(TReturn));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TReturn>)_methodPtr;
            return func(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TReturn>)_methodPtr;
            return func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TReturn>)_methodPtr;
            var obj = _handle.Target;
            return func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TReturn>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, out TReturn returnValue, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        returnValue = Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, TReturn>(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, checkSignatureMismatch);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
        T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(typeof(void));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, void>)_methodPtr;
            func(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, void>)_methodPtr;
            func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, void>)_methodPtr;
            var obj = _handle.Target;
            func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TReturn Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TReturn>(
        T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(typeof(TReturn));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TReturn>)_methodPtr;
            return func(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TReturn>)_methodPtr;
            return func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TReturn>)_methodPtr;
            var obj = _handle.Target;
            return func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TReturn>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, out TReturn returnValue, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        returnValue = Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, TReturn>(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, checkSignatureMismatch);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(
        T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(typeof(void));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, void>)_methodPtr;
            func(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, void>)_methodPtr;
            func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, void>)_methodPtr;
            var obj = _handle.Target;
            func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TReturn Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TReturn>(
        T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        
        if (checkSignatureMismatch) 
            ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(typeof(TReturn));

        if (_isStatic)
        {
            var func = (delegate* managed<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TReturn>)_methodPtr;
            return func(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15);
        }
        else if (IsValueType)
        {
            var func = (delegate* managed<ref TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TReturn>)_methodPtr;
            return func(ref Unsafe.AsRef<TTarget>(_targetPointer), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15);
        }
        else
        {
            var func = (delegate* managed<TTarget, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TReturn>)_methodPtr;
            var obj = _handle.Target;
            return func(Unsafe.As<object?, TTarget>(ref obj), arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15);
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TReturn>(T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15, out TReturn returnValue, bool checkSignatureMismatch = false)
    {
        ThrowIfDisposed();
        returnValue = Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, TReturn>(arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14, arg15, checkSignatureMismatch);
    }
    
    #endregion
    
    #region Params Invoke Method
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? Invoke(params object?[] args)
    {
        ThrowIfDisposed();
        var parameters = _method.GetParameters();
        if (parameters.Length != args.Length)
        {
            throw new ArgumentException(
                $"Parameter count mismatch: Expected {parameters.Length}, actual {args.Length}");
        }

        // 验证参数类型
        for (int i = 0; i < parameters.Length; i++)
        {
            var paramType = parameters[i].ParameterType;
            var arg = args[i];
            
            if (arg != null && !paramType.IsAssignableFrom(arg.GetType()))
            {
                throw new ArgumentException(
                    $"Parameter {i} type mismatch: Expected {paramType}, actual {arg.GetType()}");
            }
        }

        // 根据参数数量调用不同的方法
        switch (args.Length)
        {
            case 0: return Invoke<object>();
            case 1: return Invoke<object?, object?>(args[0]);
            case 2: return Invoke<object?, object?, object?>(args[0], args[1]);
            case 3: return Invoke<object?, object?, object?, object?>(args[0], args[1], args[2]);
            case 4: return Invoke<object?, object?, object?, object?, object?>(args[0], args[1], args[2], args[3]);
            case 5: return Invoke<object?, object?, object?, object?, object?, object?>(args[0], args[1], args[2], args[3], args[4]);
            case 6: return Invoke<object?, object?, object?, object?, object?, object?, object?>(args[0], args[1], args[2], args[3], args[4], args[5]);
            case 7: return Invoke<object?, object?, object?, object?, object?, object?, object?, object?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6]);
            case 8: return Invoke<object?, object?, object?, object?, object?, object?, object?, object?, object?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7]);
            case 9: return Invoke<object?, object?, object?, object?, object?, object?, object?, object?, object?, object?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8]);
            case 10: return Invoke<object?, object?, object?, object?, object?, object?, object?, object?, object?, object?, object?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9]);
            case 11: return Invoke<object?, object?, object?, object?, object?, object?, object?, object?, object?, object?, object?, object?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10]);
            case 12: return Invoke<object?, object?, object?, object?, object?, object?, object?, object?, object?, object?, object?, object?, object?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10], args[11]);
            case 13: return Invoke<object?, object?, object?, object?, object?, object?, object?, object?, object?, object?, object?, object?, object?, object?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10], args[11], args[12]);
            case 14: return Invoke<object?, object?, object?, object?, object?, object?, object?, object?, object?, object?, object?, object?, object?, object?, object?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10], args[11], args[12], args[13]);
            case 15: return Invoke<object?, object?, object?, object?, object?, object?, object?, object?, object?, object?, object?, object?, object?, object?, object?, object?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10], args[11], args[12], args[13], args[14]);
            default:
                throw new NotSupportedException($"Unsupported parameter count: {args.Length}");
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T>(params T?[] args)
    {
        ThrowIfDisposed();
        var parameters = _method.GetParameters();
        if (parameters.Length != args.Length)
        {
            throw new ArgumentException(
                $"Parameter count mismatch: Expected {parameters.Length}, actual {args.Length}");
        }

        // 验证参数类型
        for (int i = 0; i < parameters.Length; i++)
        {
            var paramType = parameters[i].ParameterType;
            var arg = args[i];
            
            if (arg != null && !paramType.IsAssignableFrom(arg.GetType()))
            {
                throw new ArgumentException(
                    $"Parameter {i} type mismatch: Expected {paramType}, actual {arg.GetType()}");
            }
        }

        // 根据参数数量调用不同的方法
        switch (args.Length)
        {
            case 0: Invoke(); break;
            case 1: Invoke(args[0]); break;
            case 2: Invoke(args[0], args[1]); break;
            case 3: Invoke(args[0], args[1], args[2]); break;
            case 4: Invoke(args[0], args[1], args[2], args[3]); break;
            case 5: Invoke(args[0], args[1], args[2], args[3], args[4]); break;
            case 6: Invoke(args[0], args[1], args[2], args[3], args[4], args[5]); break;
            case 7: Invoke(args[0], args[1], args[2], args[3], args[4], args[5], args[6]); break;
            case 8: Invoke(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7]); break;
            case 9: Invoke(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8]); break;
            case 10: Invoke(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9]); break;
            case 11: Invoke(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10]); break;
            case 12: Invoke(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10], args[11]); break;
            case 13: Invoke(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10], args[11], args[12]); break;
            case 14: Invoke(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10], args[11], args[12], args[13]); break;
            case 15: Invoke(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10], args[11], args[12], args[13], args[14]); break;
            default:
                throw new NotSupportedException($"Unsupported parameter count: {args.Length}");
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T>(out T? returnValue, params T?[] args)
    {
        ThrowIfDisposed();
        var parameters = _method.GetParameters();
        if (parameters.Length != args.Length)
        {
            throw new ArgumentException(
                $"Parameter count mismatch: Expected {parameters.Length}, actual {args.Length}");
        }

        // 验证参数类型
        for (int i = 0; i < parameters.Length; i++)
        {
            var paramType = parameters[i].ParameterType;
            var arg = args[i];
            
            if (arg != null && !paramType.IsAssignableFrom(arg.GetType()))
            {
                throw new ArgumentException(
                    $"Parameter {i} type mismatch: Expected {paramType}, actual {arg.GetType()}");
            }
        }

        // 根据参数数量调用不同的方法
        switch (args.Length)
        {
            case 0: returnValue = Invoke<T?>(); break;
            case 1: returnValue = Invoke<T?, T?>(args[0]); break;
            case 2: returnValue = Invoke<T?, T?, T?>(args[0], args[1]); break;
            case 3: returnValue = Invoke<T?, T?, T?, T?>(args[0], args[1], args[2]); break;
            case 4: returnValue = Invoke<T?, T?, T?, T?, T?>(args[0], args[1], args[2], args[3]); break;
            case 5: returnValue = Invoke<T?, T?, T?, T?, T?, T?>(args[0], args[1], args[2], args[3], args[4]); break;
            case 6: returnValue = Invoke<T?, T?, T?, T?, T?, T?, T?>(args[0], args[1], args[2], args[3], args[4], args[5]); break;
            case 7: returnValue = Invoke<T?, T?, T?, T?, T?, T?, T?, T?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6]); break;
            case 8: returnValue = Invoke<T?, T?, T?, T?, T?, T?, T?, T?, T?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7]); break;
            case 9: returnValue = Invoke<T?, T?, T?, T?, T?, T?, T?, T?, T?, T?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8]); break;
            case 10: returnValue = Invoke<T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9]); break;
            case 11: returnValue = Invoke<T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10]); break;
            case 12: returnValue = Invoke<T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10], args[11]); break;
            case 13: returnValue = Invoke<T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10], args[11], args[12]); break;
            case 14: returnValue = Invoke<T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10], args[11], args[12], args[13]); break;
            case 15: returnValue = Invoke<T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10], args[11], args[12], args[13], args[14]); break;
            default:
                throw new NotSupportedException($"Unsupported parameter count: {args.Length}");
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Invoke<T, TReturn>(out TReturn? returnValue, params T?[] args)
    {
        ThrowIfDisposed();
        var parameters = _method.GetParameters();
        if (parameters.Length != args.Length)
        {
            throw new ArgumentException(
                $"Parameter count mismatch: Expected {parameters.Length}, actual {args.Length}");
        }

        // 验证参数类型
        for (int i = 0; i < parameters.Length; i++)
        {
            var paramType = parameters[i].ParameterType;
            var arg = args[i];
            
            if (arg != null && !paramType.IsAssignableFrom(arg.GetType()))
            {
                throw new ArgumentException(
                    $"Parameter {i} type mismatch: Expected {paramType}, actual {arg.GetType()}");
            }
        }

        // 根据参数数量调用不同的方法
        switch (args.Length)
        {
            case 0: returnValue = Invoke<TReturn?>(); break;
            case 1: returnValue = Invoke<T?, TReturn?>(args[0]); break;
            case 2: returnValue = Invoke<T?, T?, TReturn?>(args[0], args[1]); break;
            case 3: returnValue = Invoke<T?, T?, T?, TReturn?>(args[0], args[1], args[2]); break;
            case 4: returnValue = Invoke<T?, T?, T?, T?, TReturn?>(args[0], args[1], args[2], args[3]); break;
            case 5: returnValue = Invoke<T?, T?, T?, T?, T?, TReturn?>(args[0], args[1], args[2], args[3], args[4]); break;
            case 6: returnValue = Invoke<T?, T?, T?, T?, T?, T?, TReturn?>(args[0], args[1], args[2], args[3], args[4], args[5]); break;
            case 7: returnValue = Invoke<T?, T?, T?, T?, T?, T?, T?, TReturn?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6]); break;
            case 8: returnValue = Invoke<T?, T?, T?, T?, T?, T?, T?, T?, TReturn?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7]); break;
            case 9: returnValue = Invoke<T?, T?, T?, T?, T?, T?, T?, T?, T?, TReturn?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8]); break;
            case 10: returnValue = Invoke<T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, TReturn?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9]); break;
            case 11: returnValue = Invoke<T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, TReturn?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10]); break;
            case 12: returnValue = Invoke<T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, TReturn?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10], args[11]); break;
            case 13: returnValue = Invoke<T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, TReturn?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10], args[11], args[12]); break;
            case 14: returnValue = Invoke<T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, TReturn?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10], args[11], args[12], args[13]); break;
            case 15: returnValue = Invoke<T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, T?, TReturn?>(args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10], args[11], args[12], args[13], args[14]); break;
            default:
                throw new NotSupportedException($"Unsupported parameter count: {args.Length}");
        }
    }
    
    #endregion
    
    #region Signature Validation
    private void ThrowIfSignatureMismatch(Type ret)
    {
        var parameters = _method.GetParameters().AsSpan();
        if (parameters.Length != 0)
        {
            throw new ArgumentException(
                $"Parameter count mismatch: Expected 0, actual {parameters.Length}");
        }

        if (_method.ReturnType != ret)
        {
            throw new ArgumentException(
                $"Return type mismatch: Expected {ret}, actual {_method.ReturnType}");
        }
    }

    private void ThrowIfSignatureMismatch<T1>(Type ret)
    {
        var parameters = _method.GetParameters().AsSpan();
        if (parameters.Length != 1)
        {
            throw new ArgumentException(
                $"Parameter count mismatch: Expected 1, actual {parameters.Length}");
        }

        if (parameters[0].ParameterType != typeof(T1))
        {
            throw new ArgumentException(
                $"Parameter 0 type mismatch: Expected {typeof(T1)}, actual {parameters[0].ParameterType}");
        }

        if (_method.ReturnType != ret)
        {
            throw new ArgumentException(
                $"Return type mismatch: Expected {ret}, actual {_method.ReturnType}");
        }
    }

    private void ThrowIfSignatureMismatch<T1, T2>(Type ret)
    {
        var parameters = _method.GetParameters().AsSpan();
        if (parameters.Length != 2)
        {
            throw new ArgumentException(
                $"Parameter count mismatch: Expected 2, actual {parameters.Length}");
        }

        if (parameters[0].ParameterType != typeof(T1))
        {
            throw new ArgumentException(
                $"Parameter 0 type mismatch: Expected {typeof(T1)}, actual {parameters[0].ParameterType}");
        }

        if (parameters[1].ParameterType != typeof(T2))
        {
            throw new ArgumentException(
                $"Parameter 1 type mismatch: Expected {typeof(T2)}, actual {parameters[1].ParameterType}");
        }

        if (_method.ReturnType != ret)
        {
            throw new ArgumentException(
                $"Return type mismatch: Expected {ret}, actual {_method.ReturnType}");
        }
    }
    
    private void ThrowIfSignatureMismatch<T1, T2, T3>(Type ret)
    {
        var parameters = _method.GetParameters().AsSpan();
        if (parameters.Length != 3)
            throw new ArgumentException($"Parameter count mismatch: Expected 3, actual {parameters.Length}");
        if (parameters[0].ParameterType != typeof(T1))
            throw new ArgumentException($"Parameter 0 type mismatch: Expected {typeof(T1)}, actual {parameters[0].ParameterType}");
        if (parameters[1].ParameterType != typeof(T2))
            throw new ArgumentException($"Parameter 1 type mismatch: Expected {typeof(T2)}, actual {parameters[1].ParameterType}");
        if (parameters[2].ParameterType != typeof(T3))
            throw new ArgumentException($"Parameter 2 type mismatch: Expected {typeof(T3)}, actual {parameters[2].ParameterType}");
        if (_method.ReturnType != ret)
            throw new ArgumentException($"Return type mismatch: Expected {ret}, actual {_method.ReturnType}");
    }
    
    private void ThrowIfSignatureMismatch<T1, T2, T3, T4>(Type ret)
    {
        var parameters = _method.GetParameters().AsSpan();
        if (parameters.Length != 4)
            throw new ArgumentException($"Parameter count mismatch: Expected 4, actual {parameters.Length}");
        if (parameters[0].ParameterType != typeof(T1))
            throw new ArgumentException($"Parameter 0 type mismatch: Expected {typeof(T1)}, actual {parameters[0].ParameterType}");
        if (parameters[1].ParameterType != typeof(T2))
            throw new ArgumentException($"Parameter 1 type mismatch: Expected {typeof(T2)}, actual {parameters[1].ParameterType}");
        if (parameters[2].ParameterType != typeof(T3))
            throw new ArgumentException($"Parameter 2 type mismatch: Expected {typeof(T3)}, actual {parameters[2].ParameterType}");
        if (parameters[3].ParameterType != typeof(T4))
            throw new ArgumentException($"Parameter 3 type mismatch: Expected {typeof(T4)}, actual {parameters[3].ParameterType}");
        if (_method.ReturnType != ret)
            throw new ArgumentException($"Return type mismatch: Expected {ret}, actual {_method.ReturnType}");
    }
    
    private void ThrowIfSignatureMismatch<T1, T2, T3, T4, T5>(Type ret)
    {
        var parameters = _method.GetParameters().AsSpan();
        if (parameters.Length != 5)
            throw new ArgumentException($"Parameter count mismatch: Expected 5, actual {parameters.Length}");
        if (parameters[0].ParameterType != typeof(T1))
            throw new ArgumentException($"Parameter 0 type mismatch: Expected {typeof(T1)}, actual {parameters[0].ParameterType}");
        if (parameters[1].ParameterType != typeof(T2))
            throw new ArgumentException($"Parameter 1 type mismatch: Expected {typeof(T2)}, actual {parameters[1].ParameterType}");
        if (parameters[2].ParameterType != typeof(T3))
            throw new ArgumentException($"Parameter 2 type mismatch: Expected {typeof(T3)}, actual {parameters[2].ParameterType}");
        if (parameters[3].ParameterType != typeof(T4))
            throw new ArgumentException($"Parameter 3 type mismatch: Expected {typeof(T4)}, actual {parameters[3].ParameterType}");
        if (parameters[4].ParameterType != typeof(T5))
            throw new ArgumentException($"Parameter 4 type mismatch: Expected {typeof(T5)}, actual {parameters[4].ParameterType}");
        if (_method.ReturnType != ret)
            throw new ArgumentException($"Return type mismatch: Expected {ret}, actual {_method.ReturnType}");
    }
    
    private void ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6>(Type ret)
    {
        var parameters = _method.GetParameters().AsSpan();
        if (parameters.Length != 6)
            throw new ArgumentException($"Parameter count mismatch: Expected 6, actual {parameters.Length}");
        if (parameters[0].ParameterType != typeof(T1)) throw new ArgumentException($"Parameter 0 type mismatch: Expected {typeof(T1)}, actual {parameters[0].ParameterType}");
        if (parameters[1].ParameterType != typeof(T2)) throw new ArgumentException($"Parameter 1 type mismatch: Expected {typeof(T2)}, actual {parameters[1].ParameterType}");
        if (parameters[2].ParameterType != typeof(T3)) throw new ArgumentException($"Parameter 2 type mismatch: Expected {typeof(T3)}, actual {parameters[2].ParameterType}");
        if (parameters[3].ParameterType != typeof(T4)) throw new ArgumentException($"Parameter 3 type mismatch: Expected {typeof(T4)}, actual {parameters[3].ParameterType}");
        if (parameters[4].ParameterType != typeof(T5)) throw new ArgumentException($"Parameter 4 type mismatch: Expected {typeof(T5)}, actual {parameters[4].ParameterType}");
        if (parameters[5].ParameterType != typeof(T6)) throw new ArgumentException($"Parameter 5 type mismatch: Expected {typeof(T6)}, actual {parameters[5].ParameterType}");
        if (_method.ReturnType != ret) throw new ArgumentException($"Return type mismatch: Expected {ret}, actual {_method.ReturnType}");
    }
    
    private void ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7>(Type ret)
    {
        var parameters = _method.GetParameters().AsSpan();
        if (parameters.Length != 7)
            throw new ArgumentException($"Parameter count mismatch: Expected 7, actual {parameters.Length}");
        if (parameters[0].ParameterType != typeof(T1)) throw new ArgumentException($"Parameter 0 type mismatch: Expected {typeof(T1)}, actual {parameters[0].ParameterType}");
        if (parameters[1].ParameterType != typeof(T2)) throw new ArgumentException($"Parameter 1 type mismatch: Expected {typeof(T2)}, actual {parameters[1].ParameterType}");
        if (parameters[2].ParameterType != typeof(T3)) throw new ArgumentException($"Parameter 2 type mismatch: Expected {typeof(T3)}, actual {parameters[2].ParameterType}");
        if (parameters[3].ParameterType != typeof(T4)) throw new ArgumentException($"Parameter 3 type mismatch: Expected {typeof(T4)}, actual {parameters[3].ParameterType}");
        if (parameters[4].ParameterType != typeof(T5)) throw new ArgumentException($"Parameter 4 type mismatch: Expected {typeof(T5)}, actual {parameters[4].ParameterType}");
        if (parameters[5].ParameterType != typeof(T6)) throw new ArgumentException($"Parameter 5 type mismatch: Expected {typeof(T6)}, actual {parameters[5].ParameterType}");
        if (parameters[6].ParameterType != typeof(T7)) throw new ArgumentException($"Parameter 6 type mismatch: Expected {typeof(T7)}, actual {parameters[6].ParameterType}");
        if (_method.ReturnType != ret) throw new ArgumentException($"Return type mismatch: Expected {ret}, actual {_method.ReturnType}");
    }
    
    private void ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7, T8>(Type ret)
    {
        var parameters = _method.GetParameters().AsSpan();
        if (parameters.Length != 8)
            throw new ArgumentException($"Parameter count mismatch: Expected 8, actual {parameters.Length}");
        if (parameters[0].ParameterType != typeof(T1)) throw new ArgumentException($"Parameter 0 type mismatch: Expected {typeof(T1)}, actual {parameters[0].ParameterType}");
        if (parameters[1].ParameterType != typeof(T2)) throw new ArgumentException($"Parameter 1 type mismatch: Expected {typeof(T2)}, actual {parameters[1].ParameterType}");
        if (parameters[2].ParameterType != typeof(T3)) throw new ArgumentException($"Parameter 2 type mismatch: Expected {typeof(T3)}, actual {parameters[2].ParameterType}");
        if (parameters[3].ParameterType != typeof(T4)) throw new ArgumentException($"Parameter 3 type mismatch: Expected {typeof(T4)}, actual {parameters[3].ParameterType}");
        if (parameters[4].ParameterType != typeof(T5)) throw new ArgumentException($"Parameter 4 type mismatch: Expected {typeof(T5)}, actual {parameters[4].ParameterType}");
        if (parameters[5].ParameterType != typeof(T6)) throw new ArgumentException($"Parameter 5 type mismatch: Expected {typeof(T6)}, actual {parameters[5].ParameterType}");
        if (parameters[6].ParameterType != typeof(T7)) throw new ArgumentException($"Parameter 6 type mismatch: Expected {typeof(T7)}, actual {parameters[6].ParameterType}");
        if (parameters[7].ParameterType != typeof(T8)) throw new ArgumentException($"Parameter 7 type mismatch: Expected {typeof(T8)}, actual {parameters[7].ParameterType}");
        if (_method.ReturnType != ret) throw new ArgumentException($"Return type mismatch: Expected {ret}, actual {_method.ReturnType}");
    }
    
    private void ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7, T8, T9>(Type ret)
    {
        var parameters = _method.GetParameters().AsSpan();
        if (parameters.Length != 9)
            throw new ArgumentException($"Parameter count mismatch: Expected 9, actual {parameters.Length}");
        if (parameters[0].ParameterType != typeof(T1)) throw new ArgumentException($"Parameter 0 type mismatch: Expected {typeof(T1)}, actual {parameters[0].ParameterType}");
        if (parameters[1].ParameterType != typeof(T2)) throw new ArgumentException($"Parameter 1 type mismatch: Expected {typeof(T2)}, actual {parameters[1].ParameterType}");
        if (parameters[2].ParameterType != typeof(T3)) throw new ArgumentException($"Parameter 2 type mismatch: Expected {typeof(T3)}, actual {parameters[2].ParameterType}");
        if (parameters[3].ParameterType != typeof(T4)) throw new ArgumentException($"Parameter 3 type mismatch: Expected {typeof(T4)}, actual {parameters[3].ParameterType}");
        if (parameters[4].ParameterType != typeof(T5)) throw new ArgumentException($"Parameter 4 type mismatch: Expected {typeof(T5)}, actual {parameters[4].ParameterType}");
        if (parameters[5].ParameterType != typeof(T6)) throw new ArgumentException($"Parameter 5 type mismatch: Expected {typeof(T6)}, actual {parameters[5].ParameterType}");
        if (parameters[6].ParameterType != typeof(T7)) throw new ArgumentException($"Parameter 6 type mismatch: Expected {typeof(T7)}, actual {parameters[6].ParameterType}");
        if (parameters[7].ParameterType != typeof(T8)) throw new ArgumentException($"Parameter 7 type mismatch: Expected {typeof(T8)}, actual {parameters[7].ParameterType}");
        if (parameters[8].ParameterType != typeof(T9)) throw new ArgumentException($"Parameter 8 type mismatch: Expected {typeof(T9)}, actual {parameters[8].ParameterType}");
        if (_method.ReturnType != ret) throw new ArgumentException($"Return type mismatch: Expected {ret}, actual {_method.ReturnType}");
    }
    
    private void ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(Type ret)
    {
        var parameters = _method.GetParameters().AsSpan();
        if (parameters.Length != 10)
            throw new ArgumentException($"Parameter count mismatch: Expected 10, actual {parameters.Length}");
        if (parameters[0].ParameterType != typeof(T1)) throw new ArgumentException($"Parameter 0 type mismatch: Expected {typeof(T1)}, actual {parameters[0].ParameterType}");
        if (parameters[1].ParameterType != typeof(T2)) throw new ArgumentException($"Parameter 1 type mismatch: Expected {typeof(T2)}, actual {parameters[1].ParameterType}");
        if (parameters[2].ParameterType != typeof(T3)) throw new ArgumentException($"Parameter 2 type mismatch: Expected {typeof(T3)}, actual {parameters[2].ParameterType}");
        if (parameters[3].ParameterType != typeof(T4)) throw new ArgumentException($"Parameter 3 type mismatch: Expected {typeof(T4)}, actual {parameters[3].ParameterType}");
        if (parameters[4].ParameterType != typeof(T5)) throw new ArgumentException($"Parameter 4 type mismatch: Expected {typeof(T5)}, actual {parameters[4].ParameterType}");
        if (parameters[5].ParameterType != typeof(T6)) throw new ArgumentException($"Parameter 5 type mismatch: Expected {typeof(T6)}, actual {parameters[5].ParameterType}");
        if (parameters[6].ParameterType != typeof(T7)) throw new ArgumentException($"Parameter 6 type mismatch: Expected {typeof(T7)}, actual {parameters[6].ParameterType}");
        if (parameters[7].ParameterType != typeof(T8)) throw new ArgumentException($"Parameter 7 type mismatch: Expected {typeof(T8)}, actual {parameters[7].ParameterType}");
        if (parameters[8].ParameterType != typeof(T9)) throw new ArgumentException($"Parameter 8 type mismatch: Expected {typeof(T9)}, actual {parameters[8].ParameterType}");
        if (parameters[9].ParameterType != typeof(T10)) throw new ArgumentException($"Parameter 9 type mismatch: Expected {typeof(T10)}, actual {parameters[9].ParameterType}");
        if (_method.ReturnType != ret) throw new ArgumentException($"Return type mismatch: Expected {ret}, actual {_method.ReturnType}");
    }
    
    private void ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(Type ret)
    {
        var parameters = _method.GetParameters().AsSpan();
        if (parameters.Length != 11)
            throw new ArgumentException($"Parameter count mismatch: Expected 11, actual {parameters.Length}");
        if (parameters[0].ParameterType != typeof(T1)) throw new ArgumentException($"Parameter 0 type mismatch: Expected {typeof(T1)}, actual {parameters[0].ParameterType}");
        if (parameters[1].ParameterType != typeof(T2)) throw new ArgumentException($"Parameter 1 type mismatch: Expected {typeof(T2)}, actual {parameters[1].ParameterType}");
        if (parameters[2].ParameterType != typeof(T3)) throw new ArgumentException($"Parameter 2 type mismatch: Expected {typeof(T3)}, actual {parameters[2].ParameterType}");
        if (parameters[3].ParameterType != typeof(T4)) throw new ArgumentException($"Parameter 3 type mismatch: Expected {typeof(T4)}, actual {parameters[3].ParameterType}");
        if (parameters[4].ParameterType != typeof(T5)) throw new ArgumentException($"Parameter 4 type mismatch: Expected {typeof(T5)}, actual {parameters[4].ParameterType}");
        if (parameters[5].ParameterType != typeof(T6)) throw new ArgumentException($"Parameter 5 type mismatch: Expected {typeof(T6)}, actual {parameters[5].ParameterType}");
        if (parameters[6].ParameterType != typeof(T7)) throw new ArgumentException($"Parameter 6 type mismatch: Expected {typeof(T7)}, actual {parameters[6].ParameterType}");
        if (parameters[7].ParameterType != typeof(T8)) throw new ArgumentException($"Parameter 7 type mismatch: Expected {typeof(T8)}, actual {parameters[7].ParameterType}");
        if (parameters[8].ParameterType != typeof(T9)) throw new ArgumentException($"Parameter 8 type mismatch: Expected {typeof(T9)}, actual {parameters[8].ParameterType}");
        if (parameters[9].ParameterType != typeof(T10)) throw new ArgumentException($"Parameter 9 type mismatch: Expected {typeof(T10)}, actual {parameters[9].ParameterType}");
        if (parameters[10].ParameterType != typeof(T11)) throw new ArgumentException($"Parameter 10 type mismatch: Expected {typeof(T11)}, actual {parameters[10].ParameterType}");
        if (_method.ReturnType != ret) throw new ArgumentException($"Return type mismatch: Expected {ret}, actual {_method.ReturnType}");
    }
    
    private void ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(Type ret)
    {
        var parameters = _method.GetParameters().AsSpan();
        if (parameters.Length != 12)
            throw new ArgumentException($"Parameter count mismatch: Expected 12, actual {parameters.Length}");
        if (parameters[0].ParameterType != typeof(T1)) throw new ArgumentException($"Parameter 0 type mismatch: Expected {typeof(T1)}, actual {parameters[0].ParameterType}");
        if (parameters[1].ParameterType != typeof(T2)) throw new ArgumentException($"Parameter 1 type mismatch: Expected {typeof(T2)}, actual {parameters[1].ParameterType}");
        if (parameters[2].ParameterType != typeof(T3)) throw new ArgumentException($"Parameter 2 type mismatch: Expected {typeof(T3)}, actual {parameters[2].ParameterType}");
        if (parameters[3].ParameterType != typeof(T4)) throw new ArgumentException($"Parameter 3 type mismatch: Expected {typeof(T4)}, actual {parameters[3].ParameterType}");
        if (parameters[4].ParameterType != typeof(T5)) throw new ArgumentException($"Parameter 4 type mismatch: Expected {typeof(T5)}, actual {parameters[4].ParameterType}");
        if (parameters[5].ParameterType != typeof(T6)) throw new ArgumentException($"Parameter 5 type mismatch: Expected {typeof(T6)}, actual {parameters[5].ParameterType}");
        if (parameters[6].ParameterType != typeof(T7)) throw new ArgumentException($"Parameter 6 type mismatch: Expected {typeof(T7)}, actual {parameters[6].ParameterType}");
        if (parameters[7].ParameterType != typeof(T8)) throw new ArgumentException($"Parameter 7 type mismatch: Expected {typeof(T8)}, actual {parameters[7].ParameterType}");
        if (parameters[8].ParameterType != typeof(T9)) throw new ArgumentException($"Parameter 8 type mismatch: Expected {typeof(T9)}, actual {parameters[8].ParameterType}");
        if (parameters[9].ParameterType != typeof(T10)) throw new ArgumentException($"Parameter 9 type mismatch: Expected {typeof(T10)}, actual {parameters[9].ParameterType}");
        if (parameters[10].ParameterType != typeof(T11)) throw new ArgumentException($"Parameter 10 type mismatch: Expected {typeof(T11)}, actual {parameters[10].ParameterType}");
        if (parameters[11].ParameterType != typeof(T12)) throw new ArgumentException($"Parameter 11 type mismatch: Expected {typeof(T12)}, actual {parameters[11].ParameterType}");
        if (_method.ReturnType != ret) throw new ArgumentException($"Return type mismatch: Expected {ret}, actual {_method.ReturnType}");
    }
    
    private void ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(Type ret)
    {
        var parameters = _method.GetParameters().AsSpan();
        if (parameters.Length != 13)
            throw new ArgumentException($"Parameter count mismatch: Expected 13, actual {parameters.Length}");
        if (parameters[0].ParameterType != typeof(T1)) throw new ArgumentException($"Parameter 0 type mismatch: Expected {typeof(T1)}, actual {parameters[0].ParameterType}");
        if (parameters[1].ParameterType != typeof(T2)) throw new ArgumentException($"Parameter 1 type mismatch: Expected {typeof(T2)}, actual {parameters[1].ParameterType}");
        if (parameters[2].ParameterType != typeof(T3)) throw new ArgumentException($"Parameter 2 type mismatch: Expected {typeof(T3)}, actual {parameters[2].ParameterType}");
        if (parameters[3].ParameterType != typeof(T4)) throw new ArgumentException($"Parameter 3 type mismatch: Expected {typeof(T4)}, actual {parameters[3].ParameterType}");
        if (parameters[4].ParameterType != typeof(T5)) throw new ArgumentException($"Parameter 4 type mismatch: Expected {typeof(T5)}, actual {parameters[4].ParameterType}");
        if (parameters[5].ParameterType != typeof(T6)) throw new ArgumentException($"Parameter 5 type mismatch: Expected {typeof(T6)}, actual {parameters[5].ParameterType}");
        if (parameters[6].ParameterType != typeof(T7)) throw new ArgumentException($"Parameter 6 type mismatch: Expected {typeof(T7)}, actual {parameters[6].ParameterType}");
        if (parameters[7].ParameterType != typeof(T8)) throw new ArgumentException($"Parameter 7 type mismatch: Expected {typeof(T8)}, actual {parameters[7].ParameterType}");
        if (parameters[8].ParameterType != typeof(T9)) throw new ArgumentException($"Parameter 8 type mismatch: Expected {typeof(T9)}, actual {parameters[8].ParameterType}");
        if (parameters[9].ParameterType != typeof(T10)) throw new ArgumentException($"Parameter 9 type mismatch: Expected {typeof(T10)}, actual {parameters[9].ParameterType}");
        if (parameters[10].ParameterType != typeof(T11)) throw new ArgumentException($"Parameter 10 type mismatch: Expected {typeof(T11)}, actual {parameters[10].ParameterType}");
        if (parameters[11].ParameterType != typeof(T12)) throw new ArgumentException($"Parameter 11 type mismatch: Expected {typeof(T12)}, actual {parameters[11].ParameterType}");
        if (parameters[12].ParameterType != typeof(T13)) throw new ArgumentException($"Parameter 12 type mismatch: Expected {typeof(T13)}, actual {parameters[12].ParameterType}");
        if (_method.ReturnType != ret) throw new ArgumentException($"Return type mismatch: Expected {ret}, actual {_method.ReturnType}");
    }
    
    private void ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(Type ret)
    {
        var parameters = _method.GetParameters().AsSpan();
        if (parameters.Length != 14)
            throw new ArgumentException($"Parameter count mismatch: Expected 14, actual {parameters.Length}");
        if (parameters[0].ParameterType != typeof(T1)) throw new ArgumentException($"Parameter 0 type mismatch: Expected {typeof(T1)}, actual {parameters[0].ParameterType}");
        if (parameters[1].ParameterType != typeof(T2)) throw new ArgumentException($"Parameter 1 type mismatch: Expected {typeof(T2)}, actual {parameters[1].ParameterType}");
        if (parameters[2].ParameterType != typeof(T3)) throw new ArgumentException($"Parameter 2 type mismatch: Expected {typeof(T3)}, actual {parameters[2].ParameterType}");
        if (parameters[3].ParameterType != typeof(T4)) throw new ArgumentException($"Parameter 3 type mismatch: Expected {typeof(T4)}, actual {parameters[3].ParameterType}");
        if (parameters[4].ParameterType != typeof(T5)) throw new ArgumentException($"Parameter 4 type mismatch: Expected {typeof(T5)}, actual {parameters[4].ParameterType}");
        if (parameters[5].ParameterType != typeof(T6)) throw new ArgumentException($"Parameter 5 type mismatch: Expected {typeof(T6)}, actual {parameters[5].ParameterType}");
        if (parameters[6].ParameterType != typeof(T7)) throw new ArgumentException($"Parameter 6 type mismatch: Expected {typeof(T7)}, actual {parameters[6].ParameterType}");
        if (parameters[7].ParameterType != typeof(T8)) throw new ArgumentException($"Parameter 7 type mismatch: Expected {typeof(T8)}, actual {parameters[7].ParameterType}");
        if (parameters[8].ParameterType != typeof(T9)) throw new ArgumentException($"Parameter 8 type mismatch: Expected {typeof(T9)}, actual {parameters[8].ParameterType}");
        if (parameters[9].ParameterType != typeof(T10)) throw new ArgumentException($"Parameter 9 type mismatch: Expected {typeof(T10)}, actual {parameters[9].ParameterType}");
        if (parameters[10].ParameterType != typeof(T11)) throw new ArgumentException($"Parameter 10 type mismatch: Expected {typeof(T11)}, actual {parameters[10].ParameterType}");
        if (parameters[11].ParameterType != typeof(T12)) throw new ArgumentException($"Parameter 11 type mismatch: Expected {typeof(T12)}, actual {parameters[11].ParameterType}");
        if (parameters[12].ParameterType != typeof(T13)) throw new ArgumentException($"Parameter 12 type mismatch: Expected {typeof(T13)}, actual {parameters[12].ParameterType}");
        if (parameters[13].ParameterType != typeof(T14)) throw new ArgumentException($"Parameter 13 type mismatch: Expected {typeof(T14)}, actual {parameters[13].ParameterType}");
        if (_method.ReturnType != ret) throw new ArgumentException($"Return type mismatch: Expected {ret}, actual {_method.ReturnType}");
    }
    
    private void ThrowIfSignatureMismatch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(Type ret)
    {
        var parameters = _method.GetParameters().AsSpan();
        if (parameters.Length != 15)
            throw new ArgumentException($"Parameter count mismatch: Expected 15, actual {parameters.Length}");
        if (parameters[0].ParameterType != typeof(T1)) throw new ArgumentException($"Parameter 0 type mismatch: Expected {typeof(T1)}, actual {parameters[0].ParameterType}");
        if (parameters[1].ParameterType != typeof(T2)) throw new ArgumentException($"Parameter 1 type mismatch: Expected {typeof(T2)}, actual {parameters[1].ParameterType}");
        if (parameters[2].ParameterType != typeof(T3)) throw new ArgumentException($"Parameter 2 type mismatch: Expected {typeof(T3)}, actual {parameters[2].ParameterType}");
        if (parameters[3].ParameterType != typeof(T4)) throw new ArgumentException($"Parameter 3 type mismatch: Expected {typeof(T4)}, actual {parameters[3].ParameterType}");
        if (parameters[4].ParameterType != typeof(T5)) throw new ArgumentException($"Parameter 4 type mismatch: Expected {typeof(T5)}, actual {parameters[4].ParameterType}");
        if (parameters[5].ParameterType != typeof(T6)) throw new ArgumentException($"Parameter 5 type mismatch: Expected {typeof(T6)}, actual {parameters[5].ParameterType}");
        if (parameters[6].ParameterType != typeof(T7)) throw new ArgumentException($"Parameter 6 type mismatch: Expected {typeof(T7)}, actual {parameters[6].ParameterType}");
        if (parameters[7].ParameterType != typeof(T8)) throw new ArgumentException($"Parameter 7 type mismatch: Expected {typeof(T8)}, actual {parameters[7].ParameterType}");
        if (parameters[8].ParameterType != typeof(T9)) throw new ArgumentException($"Parameter 8 type mismatch: Expected {typeof(T9)}, actual {parameters[8].ParameterType}");
        if (parameters[9].ParameterType != typeof(T10)) throw new ArgumentException($"Parameter 9 type mismatch: Expected {typeof(T10)}, actual {parameters[9].ParameterType}");
        if (parameters[10].ParameterType != typeof(T11)) throw new ArgumentException($"Parameter 10 type mismatch: Expected {typeof(T11)}, actual {parameters[10].ParameterType}");
        if (parameters[11].ParameterType != typeof(T12)) throw new ArgumentException($"Parameter 11 type mismatch: Expected {typeof(T12)}, actual {parameters[11].ParameterType}");
        if (parameters[12].ParameterType != typeof(T13)) throw new ArgumentException($"Parameter 12 type mismatch: Expected {typeof(T13)}, actual {parameters[12].ParameterType}");
        if (parameters[13].ParameterType != typeof(T14)) throw new ArgumentException($"Parameter 13 type mismatch: Expected {typeof(T14)}, actual {parameters[13].ParameterType}");
        if (parameters[14].ParameterType != typeof(T15)) throw new ArgumentException($"Parameter 14 type mismatch: Expected {typeof(T15)}, actual {parameters[14].ParameterType}");
        if (_method.ReturnType != ret) throw new ArgumentException($"Return type mismatch: Expected {ret}, actual {_method.ReturnType}");
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
                : _handle.Target;
#else
            object? instance = _handle.Target;
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
            throw new ObjectDisposedException(nameof(LuminFunction<TTarget>));
    }
    
    #endregion

    #region Method Caching
    
#if NET8_0_OR_GREATER
    private static (MethodInfo, MethodCache) GetCachedMethod([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] RuntimeTypeHandle type, string methodName)
#else
    private static (MethodInfo, MethodCache) GetCachedMethod(RuntimeTypeHandle type, string methodName)
#endif
    {
        return Methods.GetOrAdd((type, methodName), static key =>
        {
            var (t, name) = key;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | 
                                       BindingFlags.Instance | BindingFlags.Static;
            
            var method = Type.GetTypeFromHandle(t)!.GetMethod(name, flags)
                         ?? throw new MissingMethodException($"Method {name} not found in {t}");
            
            if (!IsValidMethod(method))
                throw new NotSupportedException($"Method {name} has unsupported signature");
            
            var cache = new MethodCache(method.IsStatic, method.MethodHandle.GetFunctionPointer());
            
            return (method, cache);
        });
    }

    private static bool IsValidMethod(MethodInfo method)
    {
        // 验证方法是否包含可接受参数
        foreach (var paramType in method.GetParameters().AsSpan())
        {
            if (paramType.ParameterType.IsByRef || paramType.ParameterType.IsPointer)
            {
                return false;
            }
        }
        
        return true;
    }
    #endregion

    [StructLayout(LayoutKind.Auto)]
    private readonly struct MethodCache
    {
        internal readonly bool IsStatic;
        internal readonly IntPtr MethodPtr;

        internal MethodCache(bool isStatic, IntPtr methodPtr)
        {
            IsStatic = isStatic;
            MethodPtr = methodPtr;
        }
    }
}