using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class MaterialReplacer
{
	[MenuItem("材质替换/替换目录下所有材质")]
	public static void ReplaceAllMaterials()
	{
		string[] matDir = new string[] { "Assets/CrytekSponza/Materials"};
		var allMats = AssetDatabase.FindAssets("t:Material", matDir);
		string[] matPath = new string[allMats.Length];
		int i = 0;
		foreach (var mat in allMats)
		{
			matPath[i++] = AssetDatabase.GUIDToAssetPath(mat);
		}

		foreach (var path in matPath)
		{
			var mat = AssetDatabase.LoadAssetAtPath(path, typeof(Material)) as Material;
			var mainTexture = mat.GetTexture("_MainTex");
			mat.shader = Shader.Find("MyPipeline/Lit");
			mat.SetTexture("_MainTex", mainTexture);
			
		}
		
		AssetDatabase.SaveAssets();
		
		
	}
}
