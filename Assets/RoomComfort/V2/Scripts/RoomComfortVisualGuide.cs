using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// A qualitative design aid. These shapes and readings are not an HVAC simulation.
[DefaultExecutionOrder(200)]
[RequireComponent(typeof(RoomComfortManualFlow))]
public class RoomComfortVisualGuide : MonoBehaviour
{
    [System.Serializable] public class Marker
    {
        public Transform target;
        public string label;
        public Vector2 screenOffset=new Vector2(0,28);
    }
    [System.Serializable] public class Probe
    {
        public string label;
        public Transform target;
        public Transform ignoreFurniture;
    }
    public Marker[] markers;
    public Probe[] probes;
    public bool directionalPreview=true;
    [Tooltip("Assign a project material using URP/Unlit, Surface Type Transparent so the shader is included in Web builds.")]
    public Material airflowMaterial;
    [Tooltip("Illustrative lengths in metres, not manufacturer performance data.")]
    public float lowReach=3,mediumReach=4.5f,highReach=6;
    [Range(0,45)] public float downwardAngle=10;
    [Range(.1f,.6f)] public float widthRatio=.3f;
    [Range(.1f,.5f)] public float heightRatio=.25f;
    public Vector3 outletLocalPosition=new Vector3(0,-.15f,.18f);
    public LayerMask obstructionLayers=~0;
    [Tooltip("Illustrative arrow animation speeds in metres per second, not measured airflow speeds.")]
    public float lowArrowSpeed=.5f,mediumArrowSpeed=.9f,highArrowSpeed=1.3f;
    readonly List<Marker> displayMarkers=new List<Marker>();
    Marker playerMarker;
    LineRenderer acOutline;
    Material selectionMaterial;
    Renderer highlightedWall;
    MaterialPropertyBlock wallBefore;
    float wallUntil,acUntil;


    RoomComfortManualFlow flow;
    Canvas canvas;
    RectTransform canvasRect,labelRoot,legend;
    RoomComfortSceneUI ui;
    TMP_Text legendHeader;
    readonly List<TMP_Text> probeRows=new List<TMP_Text>();
    readonly List<RectTransform> labels=new List<RectTransform>();
    readonly List<RectTransform> leaders=new List<RectTransform>();
    readonly List<Rect> labelRects=new List<Rect>();
    Transform air;
    Mesh mesh;
    Material fillMaterial,lineMaterial;
    readonly List<LineRenderer> lines=new List<LineRenderer>();
    float lastReach=-1,lastWidth,lastHeight,lastAngle;
    bool ready;

    void Start()
    {
        flow=GetComponent<RoomComfortManualFlow>();
        if(!flow.enabled||!flow.useScreenUI||!flow.screenPanel||!flow.screenHint)return;
        canvas=flow.screenPanel.GetComponentInParent<Canvas>();if(!canvas)return;
        canvasRect=canvas.GetComponent<RectTransform>();
        ui=flow.sceneUI;
        if(!ui)
        {
            Debug.LogError("RoomComfort scene UI is missing. Run the Scene UI migration from the RoomComfort menu before playing.",this);
            enabled=false;return;
        }
        labelRoot=ui.Get<RectTransform>("guide.labelRoot");
        legend=ui.Get<RectTransform>("guide.legend");
        legendHeader=ui.Get<TMP_Text>("guide.header");
        if(!labelRoot||!legend||!legendHeader)
        {
            Debug.LogError("RoomComfort guide UI references are incomplete. Repair the Scene UI bindings before playing.",this);
            enabled=false;return;
        }
        if(markers!=null)
        {
            for(int i=0;i<markers.Length;i++)
            {
                displayMarkers.Add(markers[i]);
                labels.Add(ui.Get<RectTransform>("marker."+i));
                leaders.Add(ui.Get<RectTransform>("marker.line."+i));
            }
        }
        playerMarker=new Marker{target=flow.player,screenOffset=new Vector2(0,-32)};
        displayMarkers.Add(playerMarker);
        labels.Add(ui.Get<RectTransform>("marker.player"));
        leaders.Add(ui.Get<RectTransform>("marker.line.player"));
        if(probes!=null)for(int i=0;i<probes.Length;i++)probeRows.Add(ui.Get<TMP_Text>("guide.probe."+i));
        air=new GameObject("Directional airflow (runtime)").transform;air.SetParent(flow.acRoot,false);
        mesh=new Mesh{name="Illustrative airflow volume"};
        air.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
        var shader=airflowMaterial?airflowMaterial.shader:Shader.Find("Universal Render Pipeline/Unlit");
        if(!shader){Debug.LogError("RoomComfortVisualGuide needs the URP Unlit shader.",this);enabled=false;return;}
        fillMaterial=airflowMaterial?new Material(airflowMaterial):new Material(shader);fillMaterial.SetColor("_BaseColor",new Color(.18f,.78f,1,.09f));
        fillMaterial.SetFloat("_Surface",1);fillMaterial.SetFloat("_Blend",0);fillMaterial.SetFloat("_ZWrite",0);
        fillMaterial.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        fillMaterial.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        fillMaterial.SetFloat("_Cull",0);fillMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");fillMaterial.renderQueue=3000;
        air.gameObject.AddComponent<MeshRenderer>().sharedMaterial=fillMaterial;
        lineMaterial=new Material(fillMaterial);lineMaterial.SetColor("_BaseColor",new Color(.15f,.8f,1));
        for(int i=0;i<7;i++)
        {
            var line=new GameObject("Airflow direction").AddComponent<LineRenderer>();line.transform.SetParent(air,false);
            line.sharedMaterial=lineMaterial;line.useWorldSpace=false;line.startWidth=line.endWidth=.025f;
            line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;line.receiveShadows=false;lines.Add(line);
        }
        acOutline=new GameObject("AC selection outline (runtime)").AddComponent<LineRenderer>();
        acOutline.transform.SetParent(flow.acRoot,false);acOutline.useWorldSpace=false;
        selectionMaterial=new Material(lineMaterial);acOutline.sharedMaterial=selectionMaterial;
        acOutline.startWidth=acOutline.endWidth=.025f;acOutline.gameObject.SetActive(false);
        ready=true;
    }
    void LateUpdate()
    {
        if(!ready)return;
        bool on=directionalPreview&&flow.power&&flow.acRoot.gameObject.activeInHierarchy;
        if(directionalPreview)flow.coverage.gameObject.SetActive(false);
        else flow.coverage.gameObject.SetActive(flow.power);
        air.gameObject.SetActive(on);
        float reach=Mathf.Max(.5f,flow.level==0?lowReach:flow.level==1?mediumReach:highReach);
        air.localPosition=outletLocalPosition;air.localRotation=Quaternion.Euler(downwardAngle,0,0);
        if(lastReach!=reach||lastWidth!=widthRatio||lastHeight!=heightRatio||lastAngle!=downwardAngle)
        {BuildAir(reach);lastReach=reach;lastWidth=widthRatio;lastHeight=heightRatio;lastAngle=downwardAngle;}
        if(on)AnimateArrows(reach);
        UpdateSelection();
        playerMarker.target=flow.IsResting?flow.ActiveRestPoint:flow.player;
        labelRoot.gameObject.SetActive(flow.IsOverview);
        if(flow.IsOverview)UpdateLabels();
        legend.gameObject.SetActive(!flow.PlannerBusy&&!flow.IsEditing);
        if(legend.gameObject.activeSelf)
        {
            string strength=ui.Message(flow.level==0?"common.low":flow.level==1?"common.medium":"common.high");
            ui.Render(legendHeader,"strength",strength);
            if(probes!=null)for(int i=0;i<probes.Length;i++)
            {
                var probe=probes[i];var row=probeRows[i];if(!row)continue;
                bool visible=probe!=null&&probe.target&&(!flow.IsResting||flow.IsOverview||probe.target==flow.ActiveRestPoint);
                row.gameObject.SetActive(visible);
                if(visible)ui.Render(row,"result",on?ReadProbe(probe,reach):ui.Message("guide.off"));
            }
        }
    }
    void UpdateLabels()
    {
        labelRects.Clear();float width=labelRoot.rect.width,height=labelRoot.rect.height;
        Camera canvasCamera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
        for(int i=0;i<labels.Count;i++)
        {
            var label=labels[i];var leader=leaders[i];
            if(!label||!leader)continue;
            var marker=displayMarkers[i];bool visible=marker!=null&&marker.target&&marker.target.gameObject.activeInHierarchy;
            Vector3 point=visible?flow.playerCamera.WorldToScreenPoint(marker.target.position):Vector3.zero;
            visible&=point.z>0&&point.x>=0&&point.x<=Screen.width&&point.y>=0&&point.y<=Screen.height;
            label.gameObject.SetActive(visible);leader.gameObject.SetActive(visible);if(!visible)continue;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(labelRoot,point,canvasCamera,out var local);
            Vector2 origin=local+labelRoot.rect.size*labelRoot.pivot;
            Vector2 size=label.rect.size;
            Vector2 minimum=Vector2.Scale(size,label.pivot)+new Vector2(8,8);
            Vector2 maximum=new Vector2(width,height)-Vector2.Scale(size,Vector2.one-label.pivot)-new Vector2(8,8);
            Vector2 position=origin+marker.screenOffset;
            position.x=maximum.x<minimum.x?width*.5f:Mathf.Clamp(position.x,minimum.x,maximum.x);
            position.y=maximum.y<minimum.y?height*.5f:Mathf.Clamp(position.y,minimum.y,maximum.y);
            for(int n=0;n<8;n++)
            {
                var box=new Rect(position-Vector2.Scale(size,label.pivot)-Vector2.one*2,size+Vector2.one*4);
                bool overlap=false;foreach(var used in labelRects)if(used.Overlaps(box)){overlap=true;break;}
                if(!overlap)break;
                position.y=maximum.y<minimum.y?height*.5f:Mathf.Clamp(position.y-size.y-4,minimum.y,maximum.y);
            }
            labelRects.Add(new Rect(position-Vector2.Scale(size,label.pivot)-Vector2.one*2,size+Vector2.one*4));
            label.anchoredPosition=position;
            Vector2 delta=position-origin;leader.anchoredPosition=origin;
            leader.sizeDelta=new Vector2(delta.magnitude,leader.sizeDelta.y);
            leader.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg);
        }
    }
    void BuildAir(float reach)
    {
        const int segments=24;var vertices=new Vector3[segments+1];vertices[0]=Vector3.zero;
        for(int i=0;i<segments;i++){float a=i*Mathf.PI*2/segments;vertices[i+1]=new Vector3(Mathf.Cos(a)*reach*widthRatio,Mathf.Sin(a)*reach*heightRatio,reach);}
        var triangles=new int[segments*3];for(int i=0;i<segments;i++){triangles[i*3]=0;triangles[i*3+1]=i+1;triangles[i*3+2]=(i+1)%segments+1;}
        mesh.Clear();mesh.vertices=vertices;mesh.triangles=triangles;mesh.RecalculateNormals();mesh.RecalculateBounds();
        for(int i=0;i<4;i++){float angle=i*Mathf.PI/2;lines[i].positionCount=2;lines[i].SetPositions(new[]{Vector3.zero,new Vector3(Mathf.Cos(angle)*reach*widthRatio,Mathf.Sin(angle)*reach*heightRatio,reach)});}
    }
    void AnimateArrows(float reach)
    {
        float speed=Mathf.Max(.05f,flow.level==0?lowArrowSpeed:flow.level==1?mediumArrowSpeed:highArrowSpeed);
        for(int i=0;i<3;i++)
        {
            float z=reach*(.15f+.8f*Mathf.Repeat(Time.unscaledTime*speed/reach+i/3f,1));float s=Mathf.Min(.22f,reach*.07f);var line=lines[4+i];line.positionCount=5;
            line.SetPositions(new[]{new Vector3(0,0,z-s*2),new Vector3(0,0,z),new Vector3(-s,0,z-s),new Vector3(0,0,z),new Vector3(s,0,z-s)});
        }
    }
    string ReadProbe(Probe probe,float reach)
    {
        var p=air.InverseTransformPoint(probe.target.position);
        if(p.z<=0||p.z>reach)return ui.Message("guide.outside");
        float width=Mathf.Max(.05f,p.z*widthRatio),height=Mathf.Max(.05f,p.z*heightRatio);
        float distance=p.x*p.x/(width*width)+p.y*p.y/(height*height);
        if(distance>1)return ui.Message("guide.outside");
        Vector3 direction=probe.target.position-air.position;
        foreach(var hit in Physics.RaycastAll(air.position,direction.normalized,direction.magnitude,obstructionLayers,QueryTriggerInteraction.Ignore))
        {
            if(hit.transform.IsChildOf(flow.acRoot)||hit.transform.IsChildOf(flow.player))continue;
            if(probe.ignoreFurniture&&hit.transform.IsChildOf(probe.ignoreFurniture))continue;
            return ui.Message("guide.blocked");
        }
        return ui.Message(distance<.3f?"guide.direct":"guide.edge");
    }
    public void HighlightWall(Transform wall)
    {
        if(!ready||!isActiveAndEnabled)return;
        RestoreWall();if(!wall)return;
        highlightedWall=wall.GetComponent<Renderer>();if(!highlightedWall)return;
        wallBefore=new MaterialPropertyBlock();highlightedWall.GetPropertyBlock(wallBefore);
        var tinted=new MaterialPropertyBlock();highlightedWall.GetPropertyBlock(tinted);
        tinted.SetColor("_BaseColor",new Color(.35f,.8f,.8f));tinted.SetColor("_Color",new Color(.35f,.8f,.8f));
        highlightedWall.SetPropertyBlock(tinted);wallUntil=Time.unscaledTime+2;HighlightAC();
    }
    public void HighlightAC(){acUntil=Time.unscaledTime+2;}
    void RestoreWall(){if(highlightedWall)highlightedWall.SetPropertyBlock(wallBefore);highlightedWall=null;wallBefore=null;}
    void UpdateSelection()
    {
        if(highlightedWall&&Time.unscaledTime>=wallUntil)RestoreWall();
        bool show=flow.acRoot.gameObject.activeInHierarchy&&(flow.IsEditing||Time.unscaledTime<acUntil);
        acOutline.gameObject.SetActive(show);if(!show)return;
        float x=flow.bodyWidth*.5f+.025f,y=flow.bodyHeight*.5f+.025f,z=.18f;
        var corners=new[]{new Vector3(-x,-y,-z),new Vector3(x,-y,-z),new Vector3(x,y,-z),new Vector3(-x,y,-z),new Vector3(-x,-y,z),new Vector3(x,-y,z),new Vector3(x,y,z),new Vector3(-x,y,z)};
        int[] order={0,1,2,3,0,4,5,1,5,6,2,6,7,3,7,4};
        acOutline.positionCount=order.Length;for(int i=0;i<order.Length;i++)acOutline.SetPosition(i,corners[order[i]]);
        selectionMaterial.SetColor("_BaseColor",flow.IsPlacementValid?new Color(.1f,1,.65f):new Color(1,.2f,.1f));
    }
    void OnDisable(){if(!Application.isPlaying)return;RestoreWall();if(acOutline)acOutline.gameObject.SetActive(false);if(labelRoot)labelRoot.gameObject.SetActive(false);if(legend)legend.gameObject.SetActive(false);if(air)air.gameObject.SetActive(false);if(flow&&flow.coverage)flow.coverage.gameObject.SetActive(flow.power);}
    void OnDestroy(){RestoreWall();if(acOutline)Destroy(acOutline.gameObject);if(selectionMaterial)Destroy(selectionMaterial);if(air)Destroy(air.gameObject);if(mesh)Destroy(mesh);if(fillMaterial)Destroy(fillMaterial);if(lineMaterial)Destroy(lineMaterial);}
}
