using UnityEngine;

// Moves an existing door/window marker along the four axis-aligned room walls.
// Linked clearance/installation areas stay separate scene objects; do not parent
// them under the non-uniformly scaled door/window cube.
public class RoomComfortWallOpening : MonoBehaviour
{
    public string displayName="Door";
    public BoxCollider clearanceArea;
    public BoxCollider installationBlocker;
    [Min(0)] public float cornerMargin=.15f;
    [Min(0)] public float surfaceGap=.002f;
    public int CurrentWall {get;private set;}
    RoomComfortManualFlow flow;
    Vector3 initialInward,initialAlong;
    Quaternion initialRotation;
    float width,height,depth,fixedHeight;
    bool initialized;
    struct AreaFrame
    {
        public BoxCollider collider;
        public float sideOffset,inset,y;
        public Quaternion rotation;
    }
    AreaFrame clearanceFrame,blockerFrame;
    [System.Serializable] public struct State
    {public Vector3 position;public Quaternion rotation;public int wall;}

    public bool Initialize(RoomComfortManualFlow owner)
    {
        if(initialized)return true;
        flow=owner;
        if(!flow||!flow.wall1||!flow.wall2||!flow.wall3||!flow.wall4)return false;
        var renderers=GetComponentsInChildren<Renderer>(true);
        if(renderers.Length==0){Debug.LogWarning(displayName+" needs a visible door/window model.",this);return false;}
        var bounds=renderers[0].bounds;foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
        CurrentWall=0;float closest=float.PositiveInfinity;
        for(int i=0;i<4;i++)
        {
            if(!WallFrame(i,out var face,out var inward,out var tangent,out _,out _,out _,out _))return false;
            float distance=Mathf.Abs(Vector3.Dot(transform.position-face,inward));
            if(distance<closest){closest=distance;CurrentWall=i;}
        }
        WallFrame(CurrentWall,out _,out initialInward,out initialAlong,out _,out _,out _,out _);
        width=Mathf.Abs(initialAlong.x)*bounds.size.x+Mathf.Abs(initialAlong.z)*bounds.size.z;
        depth=Mathf.Abs(initialInward.x)*bounds.size.x+Mathf.Abs(initialInward.z)*bounds.size.z;
        height=bounds.size.y;fixedHeight=transform.position.y;initialRotation=transform.rotation;
        clearanceFrame=CaptureArea(clearanceArea);blockerFrame=CaptureArea(installationBlocker);
        initialized=true;return true;
    }
    AreaFrame CaptureArea(BoxCollider area)
    {
        if(!area)return default;
        WallFrame(CurrentWall,out var face,out var inward,out var along,out _,out _,out _,out _);
        return new AreaFrame{collider=area,sideOffset=Vector3.Dot(area.transform.position-transform.position,along),
            inset=Vector3.Dot(area.transform.position-face,inward),y=area.transform.position.y,rotation=area.transform.rotation};
    }
    public bool PlaceOnWall(int index,Vector3 requestedPosition)
    {
        if(!initialized||!WallFrame(index,out var face,out var inward,out var along,out float min,out float max,out float bottom,out float top))return false;
        float lo=min+width*.5f+cornerMargin,hi=max-width*.5f-cornerMargin;
        if(lo>hi||fixedHeight-height*.5f<bottom-.02f||fixedHeight+height*.5f>top+.02f)return false;
        float side=Mathf.Clamp(Vector3.Dot(requestedPosition,along),lo,hi);
        var position=face+inward*(depth*.5f+surfaceGap)+along*(side-Vector3.Dot(face,along));position.y=fixedHeight;
        CurrentWall=index;transform.SetPositionAndRotation(position,WallRotation(inward)*initialRotation);FollowAreas();return true;
    }
    Quaternion WallRotation(Vector3 inward)=>Quaternion.AngleAxis(Vector3.SignedAngle(initialInward,inward,Vector3.up),Vector3.up);
    public void FollowAreas()
    {
        if(!initialized)return;MoveArea(clearanceFrame);MoveArea(blockerFrame);
        var guide=flow.GetComponent<RoomComfortVisualGuide>();
        if(guide&&guide.markers!=null)
        {
            WallFrame(CurrentWall,out _,out var inward,out _,out _,out _,out _,out _);
            foreach(var marker in guide.markers)if(marker!=null&&marker.target==transform)
                marker.screenOffset=new Vector2(inward.x*70,inward.z*32);
        }
    }
    void MoveArea(AreaFrame frame)
    {
        if(!frame.collider)return;
        WallFrame(CurrentWall,out var face,out var inward,out var along,out _,out _,out _,out _);
        float sideSign=Vector3.Dot(WallRotation(inward)*initialAlong,along);
        float side=Vector3.Dot(transform.position,along)+frame.sideOffset*sideSign;
        Vector3 p=face+inward*frame.inset+along*(side-Vector3.Dot(face,along));p.y=frame.y;
        frame.collider.transform.SetPositionAndRotation(p,WallRotation(inward)*frame.rotation);
    }
    public bool WithinWall()
    {
        if(!initialized||!WallFrame(CurrentWall,out var face,out var inward,out var along,out float min,out float max,out float bottom,out float top))return false;
        float side=Vector3.Dot(transform.position,along);
        float inset=Vector3.Dot(transform.position-face,inward);
        return side-width*.5f>=min+cornerMargin-.01f&&side+width*.5f<=max-cornerMargin+.01f
            &&transform.position.y-height*.5f>=bottom-.02f&&transform.position.y+height*.5f<=top+.02f
            &&Mathf.Abs(inset-depth*.5f-surfaceGap)<.08f;
    }
    public Bounds VisualBounds(float padding=0)
    {
        WallFrame(CurrentWall,out _,out var inward,out var along,out _,out _,out _,out _);
        return new Bounds(transform.position,new Vector3(Mathf.Abs(along.x)*width+Mathf.Abs(inward.x)*depth,height,
            Mathf.Abs(along.z)*width+Mathf.Abs(inward.z)*depth)+Vector3.one*padding*2);
    }
    public bool ContainsPointer(Vector3 point)
    {
        WallFrame(CurrentWall,out _,out var inward,out var along,out _,out _,out _,out _);
        Vector3 offset=point-transform.position;
        return Mathf.Abs(Vector3.Dot(offset,along))<=width*.5f+.12f&&Mathf.Abs(Vector3.Dot(offset,inward))<=Mathf.Max(.25f,depth*.5f);
    }
    public Vector3[] Outline()
    {
        WallFrame(CurrentWall,out _,out var inward,out var along,out _,out _,out _,out _);
        var centre=transform.position+Vector3.up*(height*.5f+.03f);
        Vector3 a=along*(width*.5f+.03f),b=inward*Mathf.Max(.12f,depth*.5f+.03f);
        return new[]{centre-a-b,centre+a-b,centre+a+b,centre-a+b};
    }
    public State Capture()=>new State{position=transform.position,rotation=transform.rotation,wall=CurrentWall};
    public void Restore(State state){CurrentWall=state.wall;transform.SetPositionAndRotation(state.position,state.rotation);FollowAreas();}
    public static void RoomLimits(RoomComfortManualFlow flow,out float xmin,out float xmax,out float zmin,out float zmax)
    {
        xmin=float.NegativeInfinity;xmax=float.PositiveInfinity;zmin=float.NegativeInfinity;zmax=float.PositiveInfinity;
        var centre=(flow.wall1.position+flow.wall2.position+flow.wall3.position+flow.wall4.position)/4;
        foreach(var wall in new[]{flow.wall1,flow.wall2,flow.wall3,flow.wall4})
        {
            var renderer=wall.GetComponent<Renderer>();if(!renderer)continue;var bounds=renderer.bounds;
            if(bounds.size.x>bounds.size.z){if(bounds.center.z<centre.z)zmin=Mathf.Max(zmin,bounds.max.z);else zmax=Mathf.Min(zmax,bounds.min.z);}
            else{if(bounds.center.x<centre.x)xmin=Mathf.Max(xmin,bounds.max.x);else xmax=Mathf.Min(xmax,bounds.min.x);}
        }
    }
    bool WallFrame(int index,out Vector3 face,out Vector3 inward,out Vector3 along,out float min,out float max,out float bottom,out float top)
    {
        face=inward=along=Vector3.zero;min=max=bottom=top=0;
        if(index<0||index>3||!flow)return false;
        var wall=new[]{flow.wall1,flow.wall2,flow.wall3,flow.wall4}[index];var renderer=wall?wall.GetComponent<Renderer>():null;if(!renderer)return false;
        var bounds=renderer.bounds;var centre=(flow.wall1.position+flow.wall2.position+flow.wall3.position+flow.wall4.position)/4;
        bool alongX=bounds.size.x>bounds.size.z;
        inward=alongX?(centre.z>bounds.center.z?Vector3.forward:Vector3.back):(centre.x>bounds.center.x?Vector3.right:Vector3.left);
        along=alongX?Vector3.right:Vector3.forward;face=bounds.center+Vector3.Scale(inward,bounds.extents);
        RoomLimits(flow,out float xmin,out float xmax,out float zmin,out float zmax);
        min=alongX?xmin:zmin;max=alongX?xmax:zmax;bottom=bounds.min.y;top=bounds.max.y;
        return !float.IsInfinity(min)&&!float.IsInfinity(max);
    }
}
