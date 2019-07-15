using System;
using UnityEngine;
using UnityEngine.Serialization;

public class InstancedColor : MonoBehaviour
{
	[SerializeField]
	Color color = Color.white;

	private void Awake()
	{
		OnValidate();
	}

	private void OnValidate()
	{
		var propertyBlock = new MaterialPropertyBlock();
		propertyBlock.SetColor("_Color", color);
		GetComponent<MeshRenderer>().SetPropertyBlock(propertyBlock);
	}
}