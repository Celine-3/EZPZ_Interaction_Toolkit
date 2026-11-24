using UnityEngine;

// Add to the furniture group, not to its individual legs, cushion or back.
public class RoomComfortFurniture : MonoBehaviour
{
    public string displayName="Furniture";
    public Vector2 footprintSize=new Vector2(1,1);
    public Vector2 footprintOffset;
    public float visualMaxHeight=1;
    public GameObject[] visualOptions;
    [HideInInspector] public int selectedOption;
    public void SetOption(int index)
    {
        if(visualOptions==null||visualOptions.Length==0)return;
        selectedOption=Mathf.Clamp(index,0,visualOptions.Length-1);
        for(int i=0;i<visualOptions.Length;i++)if(visualOptions[i])visualOptions[i].SetActive(i==selectedOption);
    }
    public Vector2[] Corners(float padding=0)
    {
        Vector3 centre=transform.TransformPoint(new Vector3(footprintOffset.x,0,footprintOffset.y));
        Vector3 x=transform.right*(footprintSize.x*Mathf.Abs(transform.lossyScale.x)*.5f+padding);
        Vector3 z=transform.forward*(footprintSize.y*Mathf.Abs(transform.lossyScale.z)*.5f+padding);
        Vector3[] p={centre-x-z,centre+x-z,centre+x+z,centre-x+z};
        var result=new Vector2[4];for(int i=0;i<4;i++)result[i]=new Vector2(p[i].x,p[i].z);return result;
    }
    [ContextMenu("Fit Visual Options To Footprint")]
    public void FitVisualOptions()
    {
        if(visualOptions==null)return;
        foreach(var option in visualOptions)
        {
            if(!option||option.transform==transform||!option.transform.IsChildOf(transform))continue;
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(option.transform,"Fit furniture visual");
#endif
            var renderers=option.GetComponentsInChildren<Renderer>(true);
            if(renderers.Length==0)continue;
            var bounds=VisualBounds(renderers);
            float factor=Mathf.Min(footprintSize.x*.96f/Mathf.Max(.001f,bounds.size.x),footprintSize.y*.96f/Mathf.Max(.001f,bounds.size.z),visualMaxHeight/Mathf.Max(.001f,bounds.size.y));
            option.transform.localScale*=factor;
            bounds=VisualBounds(renderers);
            var delta=new Vector3(footprintOffset.x-bounds.center.x,-bounds.min.y,footprintOffset.y-bounds.center.z);
            option.transform.position+=transform.TransformVector(delta);
        }
    }
    Bounds VisualBounds(Renderer[] renderers)
    {
        bool first=true;var result=new Bounds();
        foreach(var renderer in renderers)
        {
            var local=renderer.localBounds;
            for(int i=0;i<8;i++)
            {
                var corner=local.center+Vector3.Scale(local.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                var p=transform.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                if(first){result=new Bounds(p,Vector3.zero);first=false;}else result.Encapsulate(p);
            }
        }
        return result;
    }
}
