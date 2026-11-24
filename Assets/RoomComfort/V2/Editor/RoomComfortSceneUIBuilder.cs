using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class RoomComfortSceneUIBuilder
{
    static RoomComfortSceneUI ui;
    static TMP_FontAsset font;
    static RectTransform root;
    static readonly Color panelColor=new Color(.05f,.09f,.12f,.95f);

    [MenuItem("RoomComfort/Make UI Editable in Hierarchy",false,210)]
    public static void Convert()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode){Inform("RoomComfort","Stop Play mode first.","OK");return;}
        var scene=SceneManager.GetActiveScene();
        var flows=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<RoomComfortManualFlow>(true)).ToArray();
        if(flows.Length!=1){Inform("RoomComfort","Open the RoomComfort scene containing one AC_Control / Room Comfort Manual Flow.","OK");return;}
        var flow=flows[0];
        if(flow.sceneUI){Selection.activeGameObject=flow.sceneUI.gameObject;Inform("RoomComfort","This scene is already converted. Edit the existing Hierarchy objects; this command will not overwrite your changes.","OK");return;}
        var guide=flow.GetComponent<RoomComfortVisualGuide>();
        var furniture=flow.GetComponent<RoomComfortFurnitureEditor>();
        var canvas=flow.screenPanel?flow.screenPanel.GetComponentInParent<Canvas>():null;
        if(!canvas||!flow.screenHint||!guide||!furniture)
        {Inform("RoomComfort","AC_Control needs its existing Screen Panel, Screen Hint, Visual Guide and Furniture Editor assignments. No changes were made.","OK");return;}
        if(flow.screenHint.transform==flow.screenPanel.transform)
        {Inform("RoomComfort","Screen Hint must be the separate top hint text, not the AC panel.","OK");return;}
        // Validate openings without calling Initialize: that captures runtime transforms.
        var openings=furniture.wallOpenings==null?new List<RoomComfortWallOpening>():furniture.wallOpenings.Where(o=>o).Distinct().ToList();
        if(openings.Any(o=>o.GetComponentsInChildren<Renderer>(true).Length==0))
        {Inform("RoomComfort","A Door/Window entry has no model Renderer. Assign its visible model first.","OK");return;}
        Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Make RoomComfort UI editable");
        try
        {
            Undo.RecordObject(flow,"Assign scene UI");
            font=flow.screenHint.font;
            root=Rect("RoomComfort_EditableUI",canvas.transform,Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero);
            Stretch(root);
            ui=Undo.AddComponent<RoomComfortSceneUI>(root.gameObject);
            flow.sceneUI=ui;flow.useScreenUI=true;
            ui.mainButtons=flow.screenPanel.GetComponentsInChildren<Button>(true)
                .Where(b=>Enumerable.Range(0,b.onClick.GetPersistentEventCount()).Any(i=>b.onClick.GetPersistentTarget(i)==flow)).ToArray();
            BuildHints(flow);
            BuildActions(flow);
            BuildFurniture(flow,furniture,openings);
            BuildGuide(flow,guide);
            BuildMessages();
            EnsureEventSystem(scene,canvas);
            EditorUtility.SetDirty(flow);EditorUtility.SetDirty(ui);
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(undo);
            Selection.activeGameObject=root.gameObject;
            Inform("RoomComfort","UI is now stored in the Hierarchy. Press Ctrl+S to save the scene. Use RoomComfort > UI Preview to inspect each panel. Ctrl+Z undoes this conversion.","OK");
        }
        catch(Exception error)
        {
            Undo.RevertAllDownToGroup(undo);
            Debug.LogException(error);
            Inform("RoomComfort","Conversion was rolled back. See Console for the error.","OK");
        }
    }

    static void Inform(string title,string message,string button)
    {if(Application.isBatchMode)Debug.Log(message);else EditorUtility.DisplayDialog(title,message,button);}

    static void BuildHints(RoomComfortManualFlow flow)
    {
        var old=flow.screenHint;
        var background=old.transform.parent.GetComponent<RectTransform>();
        if(background&&!background.GetComponent<Canvas>()&&background.GetComponent<Image>()&&background.rect.height<156)
        {Undo.RecordObject(background,"Fit editable hint rows");background.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,156);}
        var hintRoot=Rect("EditableHints",old.transform.parent,Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero);Stretch(hintRoot);
        Undo.RecordObject(old,"Keep original hint as backup");old.enabled=false;
        var definitions=new[]{
            new[]{"walking","Walking_右键环顾说明",flow.walkingInstructions},
            new[]{"resting","Resting_坐卧说明",flow.restingInstructions},
            new[]{"overview","Overview_俯视说明",flow.overviewInstructions},
            new[]{"furniture","Furniture_布局说明","LEFT-drag the selected furniture. Rotate with the buttons. Confirm layout or Cancel / Esc."},
            new[]{"edit","AdjustPosition_拖动说明",flow.placementInstructions},
            new[]{"blocked","BlockedPosition_位置无效说明","Blocked position. Drag the AC to a clear area."}};
        foreach(var entry in definitions)
        {
            var text=TopText(entry[1],hintRoot,entry[2],-8,66,20);Bind("hint."+entry[0],text);text.gameObject.SetActive(entry[0]=="walking");
        }
        var state=TopText("CurrentState_状态格式",hintRoot,"{wall} | AC {power} | {strength}",-76,24,18);Bind("hint.state",state);
        var feedback=TopText("Feedback_结果显示",hintRoot,"Choose a wall to begin.",-102,44,18);Bind("hint.feedback",feedback);
        var heading=flow.screenPanel.transform.Find("ACpanel_title");
        TMP_Text normal=heading?heading.GetComponent<TMP_Text>():null;
        if(!normal)normal=Text("ACpanel_title",flow.screenPanel.transform,"AC controls",new Vector2(0,-12),new Vector2(264,40),24);
        Bind("heading.normal",normal);
        var edit=UnityEngine.Object.Instantiate(normal.gameObject,normal.transform.parent);Undo.RegisterCreatedObjectUndo(edit,"Create edit title");
        edit.name="AdjustPosition_Title";edit.GetComponent<TMP_Text>().text="Adjust position";Bind("heading.edit",edit.transform);edit.SetActive(false);
    }

    static void BuildActions(RoomComfortManualFlow flow)
    {
        var bar=Rect("BottomToolbar",root,new Vector2(.5f,0),new Vector2(.5f,0),new Vector2(0,14),new Vector2(756,48));Bind("actions.root",bar);
        var layout=bar.gameObject.AddComponent<HorizontalLayoutGroup>();layout.spacing=6;layout.childAlignment=TextAnchor.MiddleCenter;
        layout.childControlWidth=layout.childControlHeight=false;layout.childForceExpandWidth=layout.childForceExpandHeight=false;
        var fitter=bar.gameObject.AddComponent<ContentSizeFitter>();fitter.horizontalFit=ContentSizeFitter.FitMode.PreferredSize;
        var controls=Action(bar,"Controls","Hide controls",flow.ToggleScreenPanel);Bind("actions.controls",controls);
        var hide=controls.GetComponentInChildren<TMP_Text>();hide.name="HideControls_Text";Bind("actions.controls.hide",hide);
        var show=CopyLabel(hide,"ShowControls_Text","Show controls");Bind("actions.controls.show",show);show.gameObject.SetActive(false);
        var overview=Action(bar,"Overview","Overview",flow.ToggleOverview);Bind("actions.overview",overview);
        var enter=overview.GetComponentInChildren<TMP_Text>();enter.name="Overview_Text";Bind("actions.overview.enter",enter);
        var back=CopyLabel(enter,"ReturnToView_Text","Return to view");Bind("actions.overview.return",back);back.gameObject.SetActive(false);
        Bind("actions.sit",Action(bar,"SitDown","Sit down",flow.SitDown));
        Bind("actions.lie",Action(bar,"LieDown","Lie down",flow.LieDown));
        Bind("actions.stand",Action(bar,"StandUp","Stand up",flow.StandUp));
        Bind("actions.furniture",Action(bar,"Furniture","Furniture",flow.OpenFurniturePanel));
    }
    static Button Action(Transform parent,string name,string value,UnityAction action)
    {
        var button=Button(name,parent,value,Vector2.zero,new Vector2(120,48));
        UnityEventTools.AddPersistentListener(button.onClick,action);return button;
    }

    static void BuildFurniture(RoomComfortManualFlow flow,RoomComfortFurnitureEditor editor,List<RoomComfortWallOpening> openings)
    {
        var items=editor.furniture==null?new List<RoomComfortFurniture>():editor.furniture.Where(f=>f).ToList();
        int count=items.Count+openings.Count,rows=Mathf.Max(1,Mathf.CeilToInt(count/2f));float actionY=-(98+(rows-1)*50+54);
        var panel=Panel("FurniturePanel",root,new Vector2(0,1),new Vector2(0,1),new Vector2(16,-164),new Vector2(280,-actionY+160));Bind("furniture.panel",panel);
        Bind("furniture.instruction",Text("Instruction_结果显示",panel,"Choose furniture, Door or Window.",new Vector2(0,-12),new Vector2(256,60),17));
        int slot=0;
        for(int i=0;i<items.Count;i++)Bind("furniture.item."+i,Button("Select_"+items[i].name,panel,items[i].displayName,Selector(slot++),new Vector2(124,44)));
        for(int i=0;i<openings.Count;i++)Bind("furniture.opening."+i,Button("Select_"+openings[i].name,panel,openings[i].displayName,Selector(slot++),new Vector2(124,44)));
        Bind("furniture.rotateLeft",Button("RotateLeft",panel,"Rotate left",new Vector2(-66,actionY),new Vector2(124,44)));
        Bind("furniture.rotateRight",Button("RotateRight",panel,"Rotate right",new Vector2(66,actionY),new Vector2(124,44)));
        for(int i=0;i<4;i++)
        {var button=Button("Opening_Wall"+(i+1),panel,"Wall "+(i+1),new Vector2(-96+i*64,actionY),new Vector2(60,44),16);Bind("furniture.wall."+i,button);button.gameObject.SetActive(false);}
        Bind("furniture.confirm",Button("ConfirmLayout",panel,"Confirm layout",new Vector2(0,actionY-64),new Vector2(256,44)));
        Bind("furniture.cancel",Button("CancelLayout",panel,"Cancel",new Vector2(0,actionY-116),new Vector2(256,44)));
        panel.gameObject.SetActive(false);
        var compare=Rect("ComparisonControls",flow.screenPanel.transform,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-350),new Vector2(264,102));Bind("compare.root",compare);
        var vertical=compare.gameObject.AddComponent<VerticalLayoutGroup>();vertical.spacing=14;vertical.childControlHeight=vertical.childControlWidth=false;
        vertical.childForceExpandHeight=vertical.childForceExpandWidth=false;
        var saves=Row("Save_InOverviewOnly",compare);Bind("compare.saves",saves);
        Bind("compare.saveA",Button("SaveA",saves,"Save A",Vector2.zero,new Vector2(125,44)));
        Bind("compare.saveB",Button("SaveB",saves,"Save B",Vector2.zero,new Vector2(125,44)));
        var views=Row("View_SavedLayouts",compare);Bind("compare.views",views);
        Bind("compare.viewA",Button("ViewA",views,"View A",Vector2.zero,new Vector2(125,44)));
        Bind("compare.viewB",Button("ViewB",views,"View B",Vector2.zero,new Vector2(125,44)));
        saves.gameObject.SetActive(false);
    }
    static Vector2 Selector(int i)=>new Vector2(i%2==0?-66:66,-98-i/2*50);
    static RectTransform Row(string name,Transform parent)
    {
        var row=Rect(name,parent,new Vector2(.5f,1),new Vector2(.5f,1),Vector2.zero,new Vector2(264,44));
        var layout=row.gameObject.AddComponent<HorizontalLayoutGroup>();layout.spacing=14;layout.childControlWidth=layout.childControlHeight=false;
        layout.childForceExpandWidth=layout.childForceExpandHeight=false;return row;
    }

    static void BuildGuide(RoomComfortManualFlow flow,RoomComfortVisualGuide guide)
    {
        var labels=Rect("OverviewLabels",root,Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero);Stretch(labels);labels.SetAsFirstSibling();Bind("guide.labelRoot",labels);
        if(guide.markers!=null)for(int i=0;i<guide.markers.Length;i++)BuildMarker(labels,i.ToString(),guide.markers[i]?.label??"Marker "+(i+1),i);
        BuildMarker(labels,"player","You",guide.markers?.Length??0);labels.gameObject.SetActive(false);
        var legend=Panel("AirflowLegend",root,Vector2.zero,Vector2.zero,new Vector2(16,78),new Vector2(300,212));Bind("guide.legend",legend);
        legend.GetComponent<Image>().raycastTarget=false;
        var layout=legend.gameObject.AddComponent<VerticalLayoutGroup>();layout.padding=new RectOffset(12,12,10,10);layout.spacing=4;
        layout.childControlWidth=true;layout.childControlHeight=true;layout.childForceExpandWidth=false;layout.childForceExpandHeight=false;
        var fitter=legend.gameObject.AddComponent<ContentSizeFitter>();fitter.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        Bind("guide.header",LegendLine("Title_标题",legend,"AIRFLOW PREVIEW | {strength}",26));
        LegendLine("Arrows_箭头说明",legend,"Cyan arrows = discharge direction",40);
        if(guide.probes!=null)for(int i=0;i<guide.probes.Length;i++)
            Bind("guide.probe."+i,LegendLine("Reading_"+(guide.probes[i]?.label??i.ToString()),legend,(guide.probes[i]?.label??"Position")+" position: {result}",48));
        var footer=LegendLine("Footer_底部说明_可直接关闭",legend,"Airflow is illustrative; room\ntemperature is not calculated.",64);
        footer.gameObject.SetActive(false);
    }
    static TMP_Text LegendLine(string name,Transform parent,string value,float height)
    {
        var text=Text(name,parent,value,Vector2.zero,new Vector2(276,height),17);text.alignment=TextAlignmentOptions.TopLeft;
        var element=text.gameObject.AddComponent<LayoutElement>();element.preferredHeight=height;return text;
    }
    static void BuildMarker(Transform parent,string id,string value,int index)
    {
        var line=Rect("Leader_"+id,parent,Vector2.zero,new Vector2(0,.5f),Vector2.zero,new Vector2(30,2));
        var image=line.gameObject.AddComponent<Image>();image.color=Color.white;image.raycastTarget=false;Bind("marker.line."+id,line);
        var label=Panel("Marker_"+value,parent,Vector2.zero,new Vector2(.5f,.5f),new Vector2(100+index%5*140,200+index/5*48),new Vector2(124,32));
        label.GetComponent<Image>().raycastTarget=false;
        var text=Text("Label_直接改文字",label,value,Vector2.zero,new Vector2(124,32),18);Stretch(text.rectTransform);text.alignment=TextAlignmentOptions.Center;
        Bind("marker."+id,label);
    }
    static void BuildMessages()
    {
        var library=Rect("MessageTemplates_动态提示文字在这里改",root,new Vector2(.5f,1),new Vector2(.5f,1),new Vector2(0,-16),new Vector2(680,130));
        foreach(var group in RoomComfortUIContent.All.GroupBy(pair=>pair.Key.Split('.')[0]))
        {
            var folder=Rect(group.Key,library,Vector2.zero,Vector2.zero,Vector2.zero,Vector2.zero);Stretch(folder);
            foreach(var pair in group)
            {
                var text=TopText(pair.Key,folder,pair.Value,-12,106,20);Bind("message."+pair.Key,text);text.gameObject.SetActive(false);
            }
        }
        library.gameObject.SetActive(false);
    }

    static void EnsureEventSystem(Scene scene,Canvas canvas)
    {
        var systems=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<EventSystem>(true)).Where(s=>s.isActiveAndEnabled).ToArray();
        if(systems.Length==0)
        {
            var go=new GameObject("EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule));Undo.RegisterCreatedObjectUndo(go,"Create UI input");
            go.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
        else if(systems.Length>1)Debug.LogWarning("RoomComfort: more than one active EventSystem exists. Keep just one enabled.");
        if(!canvas.GetComponent<GraphicRaycaster>())Undo.AddComponent<GraphicRaycaster>(canvas.gameObject);
    }
    static void Bind(string id,Component component){ui.nodes.Add(new RoomComfortSceneUI.Node(id,component.gameObject));}
    static RectTransform Rect(string name,Transform parent,Vector2 anchor,Vector2 pivot,Vector2 position,Vector2 size)
    {
        var go=new GameObject(name,typeof(RectTransform));Undo.RegisterCreatedObjectUndo(go,"Create editable UI");
        var rect=go.GetComponent<RectTransform>();rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=anchor;rect.pivot=pivot;rect.anchoredPosition=position;rect.sizeDelta=size;return rect;
    }
    static void Stretch(RectTransform rect){rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
    static RectTransform Panel(string name,Transform parent,Vector2 anchor,Vector2 pivot,Vector2 position,Vector2 size)
    {var rect=Rect(name,parent,anchor,pivot,position,size);rect.gameObject.AddComponent<Image>().color=panelColor;return rect;}
    static TMP_Text Text(string name,Transform parent,string value,Vector2 position,Vector2 size,float fontSize)
    {
        var rect=Rect(name,parent,new Vector2(.5f,1),new Vector2(.5f,1),position,size);
        var text=rect.gameObject.AddComponent<TextMeshProUGUI>();text.font=font;text.text=value;text.fontSize=fontSize;text.color=Color.white;text.raycastTarget=false;text.alignment=TextAlignmentOptions.Center;return text;
    }
    static TMP_Text TopText(string name,Transform parent,string value,float y,float height,float fontSize)
    {
        var text=Text(name,parent,value,new Vector2(0,y),new Vector2(0,height),fontSize);var rect=text.rectTransform;
        rect.anchorMin=new Vector2(0,1);rect.anchorMax=new Vector2(1,1);rect.sizeDelta=new Vector2(-24,height);text.alignment=TextAlignmentOptions.TopLeft;return text;
    }
    static Button Button(string name,Transform parent,string value,Vector2 position,Vector2 size,float fontSize=18)
    {
        var rect=Rect(name,parent,new Vector2(.5f,1),new Vector2(.5f,.5f),position,size);
        var image=rect.gameObject.AddComponent<Image>();image.color=new Color(.12f,.2f,.25f,.98f);
        var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=image;
        var text=Text("Label_直接改文字",rect,value,Vector2.zero,size,fontSize);Stretch(text.rectTransform);text.rectTransform.offsetMin=new Vector2(4,2);text.rectTransform.offsetMax=new Vector2(-4,-2);
        return button;
    }
    static TMP_Text CopyLabel(TMP_Text original,string name,string value)
    {var go=UnityEngine.Object.Instantiate(original.gameObject,original.transform.parent);Undo.RegisterCreatedObjectUndo(go,"Create alternate label");go.name=name;var text=go.GetComponent<TMP_Text>();text.text=value;return text;}

    [MenuItem("RoomComfort/UI Preview/Walking",false,220)] public static void PreviewWalking()=>Preview("walking");
    [MenuItem("RoomComfort/UI Preview/Overview",false,221)] public static void PreviewOverview()=>Preview("overview");
    [MenuItem("RoomComfort/UI Preview/Adjust Position",false,222)] public static void PreviewAdjust()=>Preview("edit");
    [MenuItem("RoomComfort/UI Preview/Furniture",false,223)] public static void PreviewFurniture()=>Preview("furniture");
    [MenuItem("RoomComfort/UI Preview/Sitting or Lying",false,224)] public static void PreviewRest()=>Preview("resting");
    [MenuItem("RoomComfort/UI Preview/Selected Message",false,225)]
    public static void PreviewMessage()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode||!Selection.activeGameObject)return;
        var source=Selection.activeGameObject.GetComponent<TMP_Text>();if(!source)return;
        var owner=source.GetComponentInParent<RoomComfortSceneUI>(true);if(!owner)return;
        var destination=owner.Get<TMP_Text>("hint.feedback");if(!destination)return;
        Undo.RecordObject(destination,"Preview selected message");destination.text=source.text;
        EditorSceneManager.MarkSceneDirty(owner.gameObject.scene);
    }
    static void Preview(string mode)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)return;
        var flow=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<RoomComfortManualFlow>(true)).FirstOrDefault(f=>f.sceneUI);
        if(!flow)return;ui=flow.sceneUI;
        Undo.IncrementCurrentGroup();int undo=Undo.GetCurrentGroup();Undo.SetCurrentGroupName("Preview RoomComfort UI");
        foreach(var node in ui.nodes)if(node.target)Undo.RecordObject(node.target,"Preview UI state");
        Undo.RecordObject(flow.screenPanel,"Preview panel");
        foreach(var id in new[]{"walking","resting","overview","furniture","edit","blocked"})ui.SetVisible("hint."+id,id==mode);
        bool edit=mode=="edit",furn=mode=="furniture",rest=mode=="resting",overview=mode=="overview";
        ui.SetVisible("hint.state",!edit);ui.SetVisible("hint.feedback",!edit);
        ui.SetVisible("heading.normal",!edit);ui.SetVisible("heading.edit",edit);
        ui.SetVisible("furniture.panel",furn);ui.SetVisible("guide.labelRoot",overview);ui.SetVisible("guide.legend",!edit&&!furn);
        ui.SetVisible("compare.root",!edit&&!furn);ui.SetVisible("compare.saves",overview);
        foreach(var id in new[]{"sit","lie","furniture"})ui.SetVisible("actions."+id,!rest);
        ui.SetVisible("actions.controls.hide",!rest&&!furn);ui.SetVisible("actions.controls.show",rest||furn);
        ui.SetVisible("actions.overview.enter",!overview);ui.SetVisible("actions.overview.return",overview);
        flow.screenPanel.SetActive(!rest&&!furn);
        foreach(var button in ui.mainButtons)
        {
            if(!button)continue;Undo.RecordObject(button.gameObject,"Preview controls");
            bool placement=Enumerable.Range(0,button.onClick.GetPersistentEventCount()).Any(i=>new[]{"ConfirmPlacement","ResetPosition","CancelPlacement"}.Contains(button.onClick.GetPersistentMethodName(i)));
            button.gameObject.SetActive(placement==edit);
        }
        Canvas.ForceUpdateCanvases();EditorSceneManager.MarkSceneDirty(flow.gameObject.scene);Undo.CollapseUndoOperations(undo);
        Selection.activeGameObject=ui.gameObject;
    }
}
