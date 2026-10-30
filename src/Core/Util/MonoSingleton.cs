using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace WKMPMod.Util;

public abstract class MonoSingleton<T> : MonoBehaviour where T : MonoSingleton<T> {
	private static T _instance;
	private static readonly object _lock = new object();
	private static bool _applicationIsQuitting = false;

	public static T Instance {
		get {
			// 如果程序正在退出,不再创建新实例,防止残留
			if (_applicationIsQuitting) {
				return null;
			}

			lock (_lock) {
				if (_instance == null) {
					// 在场景中查找 挂载组件的对象
					_instance = FindObjectOfType<T>();

					// 找不到则自动创建一个 根对象并挂载组件
					if (_instance == null) {
						GameObject singleton = new GameObject(typeof(T).Name);
						_instance = singleton.AddComponent<T>();

						// 确保在场景切换时不被销毁
						DontDestroyOnLoad(singleton);
					}
				}
				return _instance;
			}
		}
	}

	protected virtual void Awake() {
		if (_instance == null) {
			_instance = (T)this;
			// 如果是手动拖入场景的,也确保跨场景持久化
			if (transform.parent == null) {
				DontDestroyOnLoad(gameObject);
			}
		} else if (_instance != this) {
			// 发现重复,立刻自毁
			Debug.LogWarning($"MonoSingleton<{typeof(T).Name}>: Duplicate components were found in the scene and have been automatically destroyed");
			Destroy(gameObject);
		}
	}

	protected virtual void OnApplicationQuit() {
		_applicationIsQuitting = true;
	}

	protected virtual void OnDestroy() {
		// 如果是当前实例被销毁,清空引用
		if (_instance == this) {
			_instance = null;
		}
	}

}