using System;
using System.Linq;
using System.Reflection;

/// <summary>
/// 通用单例基类(非MonoBehaviour)
/// - 支持私有/受保护/公开构造函数
/// - 线程安全
/// - 懒加载
/// </summary>
public abstract class Singleton<T> where T : Singleton<T> {
	// new Lazy<T>(Func<T> valueFactoey, bool isThreadSafe);
	private static readonly Lazy<T> _instance = new Lazy<T>(CreateInstance, true);

	public static T Instance => _instance.Value;

	/// <summary>
	/// 检查单例是否已创建
	/// </summary>
	public static bool IsCreated => _instance.IsValueCreated;

	private static T CreateInstance() {
		try {
			// 获取所有构造函数(包括私有)
			var constructor = typeof(T).GetConstructor(
			BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
			null, Type.EmptyTypes, null);

			if (constructor == null) {
				throw new InvalidOperationException(
					$"Singleton<{typeof(T).Name}>: Type must have a parameterless constructor");
			}

			// 调用构造函数创建实例
			return (T)constructor.Invoke(null);
		} catch (Exception ex) {
			throw new InvalidOperationException(
				$"Singleton<{typeof(T).Name}>: Failed to create instance", ex);
		}
	}

	/// <summary>
	/// 子类构造函数应该是 protected 或 private
	/// </summary>
	protected Singleton() {
		// 防止通过反射创建多个实例
		if (_instance.IsValueCreated) {
			throw new InvalidOperationException(
				$"Singleton<{typeof(T).Name}>: Instance already exists. Use {typeof(T).Name}.Instance instead.");
		}
	}
}