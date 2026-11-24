using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

[DefaultExecutionOrder(100)]
[RequireComponent(typeof(RoomComfortManualFlow))]
public class RoomComfortFurnitureEditor : MonoBehaviour
{
    public RoomComfortFurniture[] furniture;
    public BoxCollider[] keepClearAreas;
    public float rotateStep=15;
    public RoomComfortWallOpening[] wallOpenings;
    RoomComfortManualFlow flow;
    RoomComfortSceneUI ui;
    readonly List<KeyValuePair<Button,UnityEngine.Events.UnityAction>> sceneBindings=new List<KeyValuePair<Button,UnityEngine.Events.UnityAction>>();
    readonly Dictionary<Object,TMP_Text> itemLabels=new Dictionary<Object,TMP_Text>();
    RectTransform panel,comparison;
    TMP_Text instruction;
    RoomComfortFurniture selected;
    RoomComfortWallOpening selectedOpening;
    readonly List<RoomComfortWallOpening> openings=new List<RoomComfortWallOpening>();
    readonly List<Button> wallButtons=new List<Button>();
    Button rotateLeft,rotateRight,saveA,saveB,viewA,viewB;
    bool editing,dragging,available,wasInvalid;
    Vector3 grabOffset;
    LineRenderer outline;
    Material outlineMaterial;
    readonly List<RaycastResult> hits=new List<RaycastResult>();
    Layout before,optionA,optionB;
    struct Pose {public Vector3 p;public Quaternion r;public int option;}
    class Layout
    {
        public Pose[] poses;
        public RoomComfortWallOpening.State[] openings;
        public RoomComfortManualFlow.ACLayoutState ac;
    }
    bool HasSelection=>selected||selectedOpening;
    Transform SelectionTransform=>selected?selected.transform:selectedOpening?selectedOpening.transform:null;

    void Start()
    {
        flow=GetComponent<RoomComfortManualFlow>();
        if(!flow.enabled||!flow.useScreenUI||!flow.screenPanel||!flow.screenHint)return;
        if(furniture==null)furniture=new RoomComfortFurniture[0];
        if(wallOpenings!=null)foreach(var opening in wallOpenings)
            if(opening&&!openings.Contains(opening)&&opening.Initialize(flow))openings.Add(opening);
        var items=new List<RoomComfortFurniture>();
        foreach(var item in furniture)if(item){item.SetOption(item.selectedOption);items.Add(item);}
        int count=items.Count+openings.Count;
        if(count==0){Debug.LogWarning("Assign furniture or door/window objects to the layout editor.",this);return;}
        ui=flow.sceneUI;
        if(!ui){Debug.LogError("Convert this scene to editable Scene UI before using the furniture editor.",this);return;}
        panel=ui.Get<RectTransform>("furniture.panel");
        instruction=ui.Get<TMP_Text>("furniture.instruction");
        comparison=ui.Get<RectTransform>("compare.root");
        if(!panel||!instruction||!comparison){Debug.LogError("Furniture Scene UI references are incomplete. Run the Scene UI setup tool.",this);return;}
        bool complete=true;
        for(int i=0;i<items.Count;i++)
        {
            var target=items[i];var button=Bind("furniture.item."+i,()=>SelectFurniture(target));
            complete&=button!=null;if(button)itemLabels[target]=button.GetComponentInChildren<TMP_Text>(true);
        }
        for(int i=0;i<openings.Count;i++)
        {
            var target=openings[i];var button=Bind("furniture.opening."+i,()=>SelectOpening(target));
            complete&=button!=null;if(button)itemLabels[target]=button.GetComponentInChildren<TMP_Text>(true);
        }
        rotateLeft=Bind("furniture.rotateLeft",()=>Rotate(-1));
        rotateRight=Bind("furniture.rotateRight",()=>Rotate(1));
        for(int i=0;i<4;i++){int index=i;var button=Bind("furniture.wall."+i,()=>ChooseOpeningWall(index));complete&=button!=null;if(button)wallButtons.Add(button);}
        complete&=Bind("furniture.confirm",Confirm)!=null;
        complete&=Bind("furniture.cancel",Cancel)!=null;
        saveA=Bind("compare.saveA",()=>Save(false));saveB=Bind("compare.saveB",()=>Save(true));
        viewA=Bind("compare.viewA",()=>Restore(false));viewB=Bind("compare.viewB",()=>Restore(true));
        complete&=rotateLeft&&rotateRight&&saveA&&saveB&&viewA&&viewB;
        if(!complete){Unbind();Debug.LogError("Furniture Scene UI buttons are incomplete. Run the Scene UI setup tool.",this);return;}
        panel.gameObject.SetActive(false);
        outline=new GameObject("Layout selection outline (runtime)").AddComponent<LineRenderer>();
        var guide=GetComponent<RoomComfortVisualGuide>();
        outlineMaterial=guide&&guide.airflowMaterial?new Material(guide.airflowMaterial):new Material(Shader.Find("Universal Render Pipeline/Unlit"));outline.sharedMaterial=outlineMaterial;
        outline.loop=true;outline.useWorldSpace=true;outline.positionCount=4;outline.startWidth=outline.endWidth=.035f;
        outline.gameObject.SetActive(false);available=true;UpdateComparison();
    }
    Button Bind(string id,UnityEngine.Events.UnityAction action)
    {
        var button=ui.Get<Button>(id);if(!button)return null;
        button.onClick.AddListener(action);sceneBindings.Add(new KeyValuePair<Button,UnityEngine.Events.UnityAction>(button,action));return button;
    }
    void Unbind(){foreach(var binding in sceneBindings)if(binding.Key)binding.Key.onClick.RemoveListener(binding.Value);sceneBindings.Clear();}
    string SelectionName(Object item,string fallback)=>itemLabels.TryGetValue(item,out var label)&&label?label.text:fallback;
    string OptionName(bool b)=>ui.Message(b?"common.optionB":"common.optionA");
    public void BeginEdit()
    {
        if(!available||editing||!flow.BeginFurnitureEdit())return;
        before=Capture();editing=true;selected=null;selectedOpening=null;panel.gameObject.SetActive(true);
        foreach(var item in furniture)if(item){SelectFurniture(item,true);return;}
        if(openings.Count>0)SelectOpening(openings[0],true);
    }
    bool CanChangeSelection()
    {
        Physics.SyncTransforms();
        if(HasSelection&&!ValidSelection()){instruction.text=ui.Message("furniture.clearFirst");return false;}
        return true;
    }
    void SelectFurniture(RoomComfortFurniture item,bool force=false)
    {
        if(!force&&!CanChangeSelection())return;
        selected=item;selectedOpening=null;dragging=false;wasInvalid=false;UpdateActions();ShowSelectionHelp();
    }
    void SelectOpening(RoomComfortWallOpening item,bool force=false)
    {
        if(!force&&!CanChangeSelection())return;
        selected=null;selectedOpening=item;dragging=false;wasInvalid=false;UpdateActions();ShowSelectionHelp();
    }
    void UpdateActions()
    {
        rotateLeft.gameObject.SetActive(selected);rotateRight.gameObject.SetActive(selected);
        foreach(var button in wallButtons)button.gameObject.SetActive(selectedOpening);
    }
    void ShowSelectionHelp()
    {
        if(selected)instruction.text=ui.Message("furniture.dragFloor","name",SelectionName(selected,selected.displayName));
        else if(selectedOpening)instruction.text=ui.Message("furniture.dragWall","name",SelectionName(selectedOpening,selectedOpening.displayName));
    }
    void Rotate(int direction){if(selected){selected.transform.Rotate(0,rotateStep*direction,0,Space.World);Physics.SyncTransforms();}}
    void ChooseOpeningWall(int index)
    {
        if(!selectedOpening)return;
        if(!selectedOpening.PlaceOnWall(index,selectedOpening.transform.position))instruction.text=ui.Message("furniture.wallSmall");
        else ShowSelectionHelp();Physics.SyncTransforms();
    }
    bool OverUI()
    {
        if(!EventSystem.current||Mouse.current==null)return false;
        hits.Clear();EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current){position=Mouse.current.position.ReadValue()},hits);
        foreach(var hit in hits)if(hit.module is GraphicRaycaster)return true;return false;
    }
    void Update()
    {
        if(!editing)return;
        if(Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame){Cancel();return;}
        if(!HasSelection||Mouse.current==null)return;
        var mouse=Mouse.current;Ray ray=flow.playerCamera.ScreenPointToRay(mouse.position.ReadValue());
        if(mouse.leftButton.wasPressedThisFrame&&!OverUI())
        {
            var rayHits=Physics.RaycastAll(ray,100,~0,QueryTriggerInteraction.Ignore);
            System.Array.Sort(rayHits,(a,b)=>a.distance.CompareTo(b.distance));
            foreach(var hit in rayHits)
            {
                var opening=hit.transform.GetComponentInParent<RoomComfortWallOpening>();
                if(opening&&openings.Contains(opening)){SelectOpening(opening);break;}
                var item=hit.transform.GetComponentInParent<RoomComfortFurniture>();
                if(item&&System.Array.IndexOf(furniture,item)>=0){SelectFurniture(item);break;}
            }
            var plane=new Plane(Vector3.up,new Vector3(0,SelectionTransform.position.y,0));
            if(plane.Raycast(ray,out float distance))
            {
                var point=ray.GetPoint(distance);
                bool contains=selected?Inside(new Vector2(point.x,point.z),selected.Corners()):selectedOpening.ContainsPointer(point);
                if(contains){dragging=true;grabOffset=SelectionTransform.position-point;}
            }
        }
        var dragPlane=new Plane(Vector3.up,new Vector3(0,SelectionTransform.position.y,0));
        if(dragging&&mouse.leftButton.isPressed&&dragPlane.Raycast(ray,out float dist))
        {
            var p=ray.GetPoint(dist)+grabOffset;
            if(selected){p.y=selected.transform.position.y;selected.transform.position=p;}
            else selectedOpening.PlaceOnWall(selectedOpening.CurrentWall,p);
            Physics.SyncTransforms();
        }
        if(mouse.leftButton.wasReleasedThisFrame)dragging=false;
    }
    void LateUpdate()
    {
        if(!available)return;
        UpdateComparison();outline.gameObject.SetActive(editing&&HasSelection);
        if(editing&&HasSelection)
        {
            Physics.SyncTransforms();
            if(selected){var corners=selected.Corners();for(int i=0;i<4;i++)outline.SetPosition(i,new Vector3(corners[i].x,.035f,corners[i].y));}
            else outline.SetPositions(selectedOpening.Outline());
            bool valid=ValidSelection();outlineMaterial.SetColor("_BaseColor",valid?new Color(.1f,.9f,.55f):new Color(1,.2f,.1f));
            if(!valid)instruction.text=selectedOpening?ui.Message("furniture.invalidOpening"):ui.Message("furniture.invalidFurniture");
            else if(wasInvalid)ShowSelectionHelp();wasInvalid=!valid;
        }
    }
    void UpdateComparison()
    {
        comparison.gameObject.SetActive(!flow.IsEditing&&!editing);
        bool canSave=flow.IsOverview&&!flow.PlannerBusy&&!flow.IsEditing&&!editing;
        saveA.gameObject.SetActive(canSave);saveB.gameObject.SetActive(canSave);
        ui.SetVisible("compare.saves",canSave);
        viewA.interactable=optionA!=null&&!flow.IsResting;
        viewB.interactable=optionB!=null&&!flow.IsResting;
    }
    bool ValidSelection()=>selected?ValidFurniture(selected):selectedOpening&&ValidOpening(selectedOpening);
    bool ValidFurniture(RoomComfortFurniture item)
    {
        var corners=item.Corners(.04f);RoomComfortWallOpening.RoomLimits(flow,out float xmin,out float xmax,out float zmin,out float zmax);
        foreach(var p in corners)if(p.x<xmin||p.x>xmax||p.y<zmin||p.y>zmax)return false;
        foreach(var other in furniture)if(other&&other!=item&&Overlap(corners,other.Corners(.04f)))return false;
        if(keepClearAreas!=null)foreach(var area in keepClearAreas)if(area&&Overlap(corners,AreaCorners(area.bounds)))return false;
        foreach(var opening in openings)if(opening.clearanceArea&&Overlap(corners,AreaCorners(opening.clearanceArea.bounds)))return false;
        return true;
    }
    static Vector2[] AreaCorners(Bounds b)=>new[]{new Vector2(b.min.x,b.min.z),new Vector2(b.max.x,b.min.z),new Vector2(b.max.x,b.max.z),new Vector2(b.min.x,b.max.z)};
    bool ValidOpening(RoomComfortWallOpening opening)
    {
        if(!opening.WithinWall())return false;
        var bounds=opening.VisualBounds(.035f);
        foreach(var other in openings)if(other!=opening&&bounds.Intersects(other.VisualBounds(.035f)))return false;
        foreach(var item in furniture)if(item)
        {
            if(opening.clearanceArea&&Overlap(item.Corners(.04f),AreaCorners(opening.clearanceArea.bounds)))return false;
            var blocked=opening.installationBlocker?opening.installationBlocker.bounds:bounds;
            foreach(var collider in item.GetComponentsInChildren<Collider>())
                if(collider.enabled&&!collider.isTrigger&&blocked.Intersects(collider.bounds))return false;
        }
        if(flow.currentWall>=0&&flow.acRoot.gameObject.activeInHierarchy)
        {
            var right=flow.acRoot.right;var forward=flow.acRoot.forward;
            var acBounds=new Bounds(flow.acRoot.position,new Vector3(Mathf.Abs(right.x)*flow.bodyWidth+Mathf.Abs(forward.x)*.45f,flow.bodyHeight,Mathf.Abs(right.z)*flow.bodyWidth+Mathf.Abs(forward.z)*.45f));
            if((opening.installationBlocker?opening.installationBlocker.bounds:bounds).Intersects(acBounds))return false;
        }
        return true;
    }
    static bool Inside(Vector2 p,Vector2[] c)
    {
        float sign=0;for(int i=0;i<4;i++){Vector2 a=c[(i+1)%4]-c[i],b=p-c[i];float cross=a.x*b.y-a.y*b.x;if(Mathf.Abs(cross)<.0001f)continue;if(sign==0)sign=Mathf.Sign(cross);else if(sign*Mathf.Sign(cross)<0)return false;}return true;
    }
    static bool Overlap(Vector2[] a,Vector2[] b)
    {
        foreach(var polygon in new[]{a,b})for(int i=0;i<2;i++)
        {
            var edge=polygon[i+1]-polygon[i];var axis=new Vector2(-edge.y,edge.x).normalized;
            float amin=float.PositiveInfinity,amax=float.NegativeInfinity,bmin=amin,bmax=amax;
            foreach(var p in a){float d=Vector2.Dot(p,axis);amin=Mathf.Min(amin,d);amax=Mathf.Max(amax,d);}
            foreach(var p in b){float d=Vector2.Dot(p,axis);bmin=Mathf.Min(bmin,d);bmax=Mathf.Max(bmax,d);}
            if(amax<=bmin||bmax<=amin)return false;
        }
        return true;
    }
    Layout Capture()
    {
        var data=new Layout{poses=new Pose[furniture.Length],openings=new RoomComfortWallOpening.State[openings.Count],ac=flow.CaptureACLayout()};
        for(int i=0;i<furniture.Length;i++)if(furniture[i])data.poses[i]=new Pose{p=furniture[i].transform.position,r=furniture[i].transform.rotation,option=furniture[i].selectedOption};
        for(int i=0;i<openings.Count;i++)data.openings[i]=openings[i].Capture();return data;
    }
    void Apply(Layout data,bool restoreAC)
    {
        for(int i=0;i<furniture.Length;i++)if(furniture[i]){var p=data.poses[i];furniture[i].transform.SetPositionAndRotation(p.p,p.r);furniture[i].SetOption(p.option);}
        for(int i=0;i<openings.Count;i++)openings[i].Restore(data.openings[i]);
        Physics.SyncTransforms();if(restoreAC)flow.RestoreACLayout(data.ac);
    }
    bool ValidateLayout(bool selectInvalid)
    {
        Physics.SyncTransforms();
        foreach(var item in furniture)if(item&&!ValidFurniture(item))
        {if(selectInvalid)SelectFurniture(item,true);return false;}
        foreach(var opening in openings)if(!ValidOpening(opening))
        {if(selectInvalid)SelectOpening(opening,true);return false;}
        return true;
    }
    public void Confirm()
    {
        if(!editing)return;
        if(!ValidateLayout(true)){instruction.text=ui.Message("furniture.correctBeforeConfirm");return;}
        if(!flow.ResolveStandingSpace()){instruction.text=ui.Message("furniture.keepStandingSpace");return;}
        editing=false;dragging=false;panel.gameObject.SetActive(false);flow.EndFurnitureEdit(true);
        flow.Notify(ui.Message("furniture.layoutUpdated"));
    }
    public void Cancel(){if(!editing)return;Apply(before,false);editing=false;dragging=false;panel.gameObject.SetActive(false);flow.EndFurnitureEdit(false);}
    void Save(bool b)
    {
        if(!flow.IsOverview||editing||flow.IsEditing||flow.PlannerBusy)return;
        if(!ValidateLayout(false)||(flow.currentWall>=0&&!flow.IsPlacementValid))
        {flow.Notify(ui.Message("furniture.correctBeforeSave"));return;}
        if(b)optionB=Capture();else optionA=Capture();
        flow.Notify(ui.Message("furniture.saved","option",OptionName(b)));
    }
    void Restore(bool b)
    {
        if(editing||flow.IsEditing||flow.PlannerBusy)return;
        if(flow.IsResting){flow.Notify(ui.Message("furniture.standBeforeRestore"));return;}
        var data=b?optionB:optionA;if(data==null){flow.Notify(ui.Message("furniture.saveFirst","option",OptionName(b)));return;}
        var rollback=Capture();Apply(data,true);
        if(!flow.ResolveStandingSpace()){Apply(rollback,true);flow.Notify(ui.Message("furniture.noStandingSpace"));return;}
        flow.Notify(ui.Message("furniture.viewing","option",OptionName(b)));
    }
    void OnDestroy(){Unbind();if(outline)Destroy(outline.gameObject);if(outlineMaterial)Destroy(outlineMaterial);}
}
