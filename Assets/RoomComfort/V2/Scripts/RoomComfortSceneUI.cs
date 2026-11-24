using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Bindings only: the editable objects and all their text are stored in the scene.
public class RoomComfortSceneUI : MonoBehaviour
{
    [Serializable] public class Node
    {
        public string id;
        public GameObject target;
        public Node(string id,GameObject target){this.id=id;this.target=target;}
    }
    [HideInInspector] public List<Node> nodes=new List<Node>();
    [HideInInspector] public Button[] mainButtons;
    [HideInInspector] public int version=1;
    readonly Dictionary<string,GameObject> lookup=new Dictionary<string,GameObject>();
    readonly Dictionary<TMP_Text,string> patterns=new Dictionary<TMP_Text,string>();
    readonly HashSet<string> warnings=new HashSet<string>();
    void Awake(){lookup.Clear();patterns.Clear();warnings.Clear();}
    public bool ValidateBindings(params string[] ids)
    {
        bool valid=true;
        foreach(var id in ids)if(!nodes.Exists(n=>n!=null&&n.id==id&&n.target))
        {valid=false;Debug.LogError("RoomComfort UI object is missing: "+id+". Restore the deleted object with Undo or reopen your saved scene.",this);}
        return valid;
    }
    public T Get<T>(string id) where T:Component
    {
        if(lookup.Count==0)foreach(var node in nodes)if(node!=null&&node.target)lookup[node.id]=node.target;
        if(lookup.TryGetValue(id,out var target)&&target)return target.GetComponent<T>();
        if(warnings.Add(id))Debug.LogError("Missing RoomComfort UI binding: "+id,this);
        return null;
    }
    public void SetVisible(string id,bool visible)
    {var target=Get<Transform>(id);if(target&&target.gameObject.activeSelf!=visible)target.gameObject.SetActive(visible);}
    public string Message(string key,params string[] pairs)
    {var source=Get<TMP_Text>("message."+key);return source?Format(source.text,pairs):"";}
    public void Render(TMP_Text destination,params string[] pairs)
    {
        if(!destination)return;
        if(!patterns.TryGetValue(destination,out var pattern)){pattern=destination.text;patterns[destination]=pattern;}
        destination.text=Format(pattern,pairs);
    }
    public static string Format(string template,params string[] pairs)
    {
        if(string.IsNullOrEmpty(template))return "";
        // Token scanning prevents a replacement value containing braces being replaced a second time.
        var values=new Dictionary<string,string>();
        for(int i=0;i+1<pairs.Length;i+=2)values[pairs[i]]=pairs[i+1]??"";
        return System.Text.RegularExpressions.Regex.Replace(template,@"\{([^{}]+)\}",m=>values.TryGetValue(m.Groups[1].Value,out var value)?value:m.Value);
    }
}
