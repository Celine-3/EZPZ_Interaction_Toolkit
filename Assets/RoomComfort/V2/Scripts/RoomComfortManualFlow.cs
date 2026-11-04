using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Attach to an always-active FlowManager, NOT to a step panel or the AC.
// This component is for the user's existing, manually arranged EZPZ scene.
public class RoomComfortManualFlow : MonoBehaviour
{
    [Header("Hint text ONLY. Never assign a parent containing buttons to these four slots.")]
    public GameObject step1, step2, step3Instructions, step4;
    [Header("All permanent buttons: wall choices, power, strength, edit and comparison")]
    public GameObject previewControls;
    public Transform acRoot, coverage;
    public Transform wall1, wall2, wall3, wall4;
    public Transform seatView, seatExit, bedView, bedExit;
    public Transform player;
    public Camera playerCamera;
    public MonoBehaviour movementController;
    public MonoBehaviour raycastInteractor;
    public GameObject seatPrompt, bedPrompt;
    [InspectorName("坐下／躺下操作提示")]
    [TextArea(3, 6)]
    public string restingInstructions = "Hold RIGHT mouse to look around.\nRelease to LEFT-click controls. Stand up / Esc to leave.";
    public float nearDistance = 2.2f;
    public float minimumMountHeight = 1.9f;
    public float bodyWidth = 1.3f, bodyHeight = .4f, wallStandOff = .2f;
    [Tooltip("Box colliders defining forbidden installation areas, e.g. a door/window. Optional.")]
    public BoxCollider[] blockedAreas;
    public float lowScale = .75f, mediumScale = 1f, highScale = 1.25f;

    public int currentStep = 1, currentWall = -1, level = 1;
    public bool power;
    Vector3 originalCoverageScale, roomCentre;
    Vector3 cameraPosition, unitPosition;
    Quaternion cameraRotation, unitRotation;
    bool editing, dragging, confirmed, resting, valid;
    bool oldMovement, oldRay, oldCharacter;
    CursorLockMode oldCursor; bool oldVisible;
    readonly List<Behaviour> cameraBehaviours = new List<Behaviour>();
    CharacterController character;
    Transform restingExit;
    float lookYaw, lookPitch;
    Vector3 restEye;
    string status="Choose a wall to begin.";
    bool saved;
    int savedWall, savedLevel;
    Vector3 savedPosition; Quaternion savedRotation;
    int rollbackWall;
    Vector3 rollbackPosition; Quaternion rollbackRotation;
    bool rollbackExists;
    bool editFromRest, rollbackPower;
    readonly bool[] remembered = new bool[4];
    readonly Vector3[] wallPositions = new Vector3[4];
    float ignoreClicksUntil;
    Bounds bounds;
    Vector3 inward, horizontal, planeCentre;
    float horizontalMin, horizontalMax, verticalMin, verticalMax;
    LineRenderer mountingOutline;

    void Start()
    {
        if(!acRoot||!coverage||!wall1||!wall2||!wall3||!wall4||!player||!playerCamera||!movementController||!raycastInteractor)
        {Debug.LogError("RoomComfortManualFlow: assign AC, coverage, four walls, player, camera and both controller fields.",this);enabled=false;return;}
        originalCoverageScale=coverage.localScale;
        roomCentre=(wall1.position+wall2.position+wall3.position+wall4.position)/4;
        character=player.GetComponent<CharacterController>();
        ShowHint(1);Set(previewControls,true);
        Set(seatPrompt,false);Set(bedPrompt,false);acRoot.gameObject.SetActive(false);coverage.gameObject.SetActive(false);
    }
    void Set(GameObject g,bool on){if(g)g.SetActive(on);}
    void ShowHint(int hint)
    {
        currentStep=hint;Set(step1,hint==1);Set(step2,hint==2);Set(step3Instructions,hint==3);Set(step4,hint==4);
    }
    public void ChooseWall1()=>ChooseWall(0);
    public void ChooseWall2()=>ChooseWall(1);
    public void ChooseWall3()=>ChooseWall(2);
    public void ChooseWall4()=>ChooseWall(3);

    void Freeze()
    {
        oldMovement=movementController.enabled;oldRay=raycastInteractor.enabled;
        movementController.enabled=false;raycastInteractor.enabled=false;
        if(character){oldCharacter=character.enabled;character.enabled=false;}
        cameraPosition=playerCamera.transform.position;cameraRotation=playerCamera.transform.rotation;
        cameraBehaviours.Clear();
        foreach(var b in playerCamera.GetComponents<Behaviour>())
            if(b.enabled&&b.GetType().Name.Contains("CinemachineBrain")){cameraBehaviours.Add(b);b.enabled=false;}
        oldCursor=Cursor.lockState;oldVisible=Cursor.visible;
        Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
    }
    void Unfreeze()
    {
        playerCamera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);
        foreach(var b in cameraBehaviours)if(b)b.enabled=true;cameraBehaviours.Clear();
        if(character)character.enabled=oldCharacter;
        movementController.enabled=oldMovement;raycastInteractor.enabled=oldRay;
        Cursor.lockState=oldCursor;Cursor.visible=oldVisible;
    }
    bool ConfigureWall(int index)
    {
        Transform w=new[]{wall1,wall2,wall3,wall4}[index];var r=w.GetComponent<Renderer>();
        if(!r){status="Each wall must reference its actual wall mesh, not an empty group.";return false;}
        bounds=r.bounds;
        // Supports the four axis-aligned walls in the existing RoomComfort scene.
        bool alongX=bounds.size.x>bounds.size.z;
        inward=alongX?(roomCentre.z>w.position.z?Vector3.forward:Vector3.back):(roomCentre.x>w.position.x?Vector3.right:Vector3.left);
        horizontal=alongX?Vector3.right:Vector3.forward;
        planeCentre=bounds.center+Vector3.Scale(inward,bounds.extents)+inward*wallStandOff;
        horizontalMin=(alongX?bounds.min.x:bounds.min.z)+bodyWidth/2+.12f;
        horizontalMax=(alongX?bounds.max.x:bounds.max.z)-bodyWidth/2-.12f;
        verticalMin=Mathf.Max(minimumMountHeight,bounds.min.y+bodyHeight/2+.12f);
        verticalMax=bounds.max.y-bodyHeight/2-.12f;
        return horizontalMax>horizontalMin&&verticalMax>verticalMin;
    }
    public void ChooseWall(int index)
    {
        if(editing){status="Finish or cancel the current drag before changing walls.";return;}
        if(index<0||index>3||!ConfigureWall(index))return;
        if(currentWall>=0&&confirmed){remembered[currentWall]=true;wallPositions[currentWall]=acRoot.position;}
        currentWall=index;power=false;coverage.gameObject.SetActive(false);acRoot.gameObject.SetActive(true);
        acRoot.rotation=Quaternion.LookRotation(inward);
        SetPosition(remembered[index]?wallPositions[index]:planeCentre+Vector3.up*(Mathf.Clamp(2.4f,verticalMin,verticalMax)-planeCentre.y));
        confirmed=true;ShowHint(2);Set(previewControls,true);
        status=valid?"Wall "+(index+1)+" selected. AC is OFF. Turn it on to compare; your viewpoint has not moved.":"This position overlaps a blocked area. Use Adjust position before turning on.";
    }
    void ShowOutline()
    {
        if(!mountingOutline)
        {
            var g=new GameObject("Mounting area outline");g.transform.SetParent(transform);mountingOutline=g.AddComponent<LineRenderer>();
            mountingOutline.sharedMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));mountingOutline.sharedMaterial.color=new Color(.1f,.75f,.6f);
            mountingOutline.startWidth=mountingOutline.endWidth=.025f;mountingOutline.useWorldSpace=true;mountingOutline.loop=true;mountingOutline.positionCount=4;
        }
        Vector3 a=planeCentre;a.y=verticalMin;a+=horizontal*(horizontalMin-Vector3.Dot(a,horizontal));
        mountingOutline.SetPositions(new[]{a,a+horizontal*(horizontalMax-horizontalMin),a+horizontal*(horizontalMax-horizontalMin)+Vector3.up*(verticalMax-verticalMin),a+Vector3.up*(verticalMax-verticalMin)});
        mountingOutline.gameObject.SetActive(true);
    }
    void SetPosition(Vector3 p)
    {
        float u=Mathf.Clamp(Vector3.Dot(p,horizontal),horizontalMin,horizontalMax);
        unitPosition=planeCentre+horizontal*(u-Vector3.Dot(planeCentre,horizontal));unitPosition.y=Mathf.Clamp(p.y,verticalMin,verticalMax);
        unitRotation=Quaternion.LookRotation(inward);acRoot.SetPositionAndRotation(unitPosition,unitRotation);
        valid=true;var candidate=new Bounds(unitPosition,Vector3.Scale(horizontal,Vector3.one*bodyWidth)+Vector3.up*bodyHeight+new Vector3(Mathf.Abs(inward.x),0,Mathf.Abs(inward.z))*.45f);
        if(blockedAreas!=null)foreach(var b in blockedAreas)if(b&&b.bounds.Intersects(candidate))valid=false;
    }
    public void ConfirmPlacement()
    {
        if(!editing||!valid)return;
        editing=false;dragging=false;confirmed=true;
        remembered[currentWall]=true;wallPositions[currentWall]=acRoot.position;
        if(mountingOutline)mountingOutline.gameObject.SetActive(false);
        if(!editFromRest)Unfreeze();editFromRest=false;ShowHint(2);
        status="Position saved. Turn AC On to view it. You can change walls at any time.";
    }
    public void CancelPlacement()
    {
        if(!editing)return;
        editing=false;dragging=false;if(mountingOutline)mountingOutline.gameObject.SetActive(false);
        if(!editFromRest)Unfreeze();editFromRest=false;
        if(rollbackExists){currentWall=rollbackWall;ConfigureWall(currentWall);SetPosition(rollbackPosition);confirmed=true;power=rollbackPower;coverage.gameObject.SetActive(power);ShowHint(power?(resting?4:3):2);}
        else{currentWall=-1;confirmed=false;acRoot.gameObject.SetActive(false);ShowHint(1);}
        status="Adjustment cancelled. Your previous position was restored.";
    }
    public void EditPosition()
    {
        if(!confirmed||editing){status="Choose a wall before adjusting the AC.";return;}
        if(!ConfigureWall(currentWall))return;
        rollbackExists=true;rollbackWall=currentWall;rollbackPosition=acRoot.position;rollbackRotation=acRoot.rotation;rollbackPower=power;
        editFromRest=resting;if(!resting)Freeze();
        editing=true;power=false;coverage.gameObject.SetActive(false);ignoreClicksUntil=Time.unscaledTime+.25f;
        SetPosition(acRoot.position);ShowOutline();status="Drag to adjust. Save position or cancel; this does not lock your wall choice.";
    }
    public void ResetPosition(){if(editing)SetPosition(planeCentre+Vector3.up*(Mathf.Clamp(2.4f,verticalMin,verticalMax)-planeCentre.y));}
    public void ChooseAnotherWall()
    {
        status="Click Wall 1, 2, 3 or 4 directly. No reset or standing up is needed.";
    }
    public void ACOn()
    {
        if(editing){status="Save or cancel the position adjustment first.";return;}
        if(!confirmed){status="Choose a wall first; then turn AC On.";return;}
        if(!valid){status="Adjust the AC clear of the blocked area first.";return;}
        power=true;ApplyStrength();coverage.gameObject.SetActive(true);
        ShowHint(resting?4:3);status=resting?"Compare from this same viewpoint. Try another wall whenever you want.":"Preview on. Walk to the bed or chair, or try another wall now.";
    }
    public void ACOff(){power=false;coverage.gameObject.SetActive(false);if(confirmed&&!editing)ShowHint(2);status="AC off. The unit, selected strength and your viewpoint are unchanged.";}
    public void Low()=>SetStrength(0);
    public void Medium()=>SetStrength(1);
    public void High()=>SetStrength(2);
    void SetStrength(int i){level=i;ApplyStrength();status=new[]{"Low","Medium","High"}[level]+" selected. "+(power?"Preview updated.":"AC remains off.");}
    void ApplyStrength(){coverage.localScale=originalCoverageScale*new[]{lowScale,mediumScale,highScale}[level];}
    public void SaveOption(){if(!confirmed||editing)return;saved=true;savedWall=currentWall;savedPosition=acRoot.position;savedRotation=acRoot.rotation;savedLevel=level;status="Option A saved for this session.";}
    public void RestoreOption(){if(!saved||editing)return;currentWall=savedWall;ConfigureWall(currentWall);SetPosition(savedPosition);level=savedLevel;acRoot.gameObject.SetActive(true);confirmed=true;ApplyStrength();power=false;coverage.gameObject.SetActive(false);ShowHint(2);status="Option A restored. Turn AC On to compare from your current viewpoint.";}
    bool Near(Transform point)=>point&&Vector2.Distance(new Vector2(player.position.x,player.position.z),new Vector2(point.position.x,point.position.z))<nearDistance;
    public void SitDown()=>Rest(false);
    public void LieDown()=>Rest(true);
    void Rest(bool lie)
    {
        var eye=lie?bedView:seatView;var exit=lie?bedExit:seatExit;
        if(editing||resting||!eye||!exit||!Near(eye))return;
        Freeze();resting=true;restingExit=exit;restEye=eye.position;lookYaw=eye.eulerAngles.y;lookPitch=Mathf.DeltaAngle(0,eye.eulerAngles.x);
        Set(seatPrompt,false);Set(bedPrompt,false);
        if(confirmed&&power)ShowHint(4);status="Stay here and compare walls with the controls. Stand up whenever you want.";
    }
    public void StandUp()
    {
        if(!resting)return;
        resting=false;Vector3 delta=restingExit.position-player.position;player.position=restingExit.position;cameraPosition+=delta;Unfreeze();
    }
    void Update()
    {
        Set(seatPrompt,!editing&&!resting&&Near(seatView));Set(bedPrompt,!editing&&!resting&&Near(bedView));
        var m=Mouse.current;var k=Keyboard.current;if(m==null||k==null)return;
        if(k.escapeKey.wasPressedThisFrame){if(editing)CancelPlacement();else if(resting)StandUp();}
        if(editing)
        {
            var pointer=m.position.ReadValue();
            bool overToolbar=pointer.y>Screen.height-115;
            if(m.leftButton.wasPressedThisFrame&&!overToolbar&&Time.unscaledTime>ignoreClicksUntil&&Physics.Raycast(playerCamera.ScreenPointToRay(pointer),out var hit,100)&&hit.transform.IsChildOf(acRoot))dragging=true;
            if(dragging&&m.leftButton.isPressed)
            {
                var ray=playerCamera.ScreenPointToRay(pointer);var plane=new Plane(inward,planeCentre);
                if(plane.Raycast(ray,out float d))SetPosition(ray.GetPoint(d));
            }
            if(m.leftButton.wasReleasedThisFrame)dragging=false;
        }
        if(resting&&m.rightButton.isPressed){var d=m.delta.ReadValue();lookYaw+=d.x*.1f;lookPitch=Mathf.Clamp(lookPitch-d.y*.1f,-75,75);}
    }
    void LateUpdate()
    {
        if(editing)
        {
            float span=horizontalMax-horizontalMin+bodyWidth;
            float distance=Mathf.Max(3,span/(2*Mathf.Tan(playerCamera.fieldOfView*Mathf.Deg2Rad*.5f)*playerCamera.aspect)*1.3f);
            playerCamera.transform.position=planeCentre+inward*distance+Vector3.up*((verticalMin+verticalMax)/2-planeCentre.y);
            playerCamera.transform.rotation=Quaternion.LookRotation(-inward);
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
        }
        if(resting&&!editing){playerCamera.transform.position=restEye;playerCamera.transform.rotation=Quaternion.Euler(lookPitch,lookYaw,0);Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
    }
    void OnGUI()
    {
        if(!editing&&!resting)
        {
            GUI.Box(new Rect(12,Screen.height-57,Mathf.Min(Screen.width-24,720),45),
                (currentWall<0?"No wall selected":"Wall "+(currentWall+1))+" | "+(power?"AC ON":"AC OFF")+" | "+new[]{"Low","Medium","High"}[level]+"\n"+status);
            return;
        }
        GUILayout.BeginArea(new Rect(12,12,Mathf.Min(Screen.width-24,700),resting&&!editing?220:100),GUI.skin.box);
        if(editing)
        {
            GUILayout.Label(valid?"Drag the AC on the wall. Release, then confirm.":"This position overlaps a blocked area.");
            GUILayout.BeginHorizontal();GUI.enabled=valid;if(GUILayout.Button("Save position",GUILayout.Height(32)))ConfirmPlacement();GUI.enabled=true;
            if(GUILayout.Button("Reset position",GUILayout.Height(32)))ResetPosition();
            if(GUILayout.Button("Cancel",GUILayout.Height(32)))CancelPlacement();GUILayout.EndHorizontal();
        }
        else
        {
            GUILayout.Label(restingInstructions);
            GUILayout.BeginHorizontal();for(int i=0;i<4;i++)if(GUILayout.Button("Wall "+(i+1),GUILayout.Height(28)))ChooseWall(i);GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if(GUILayout.Button(power?"AC Off":"AC On",GUILayout.Height(32))){if(power)ACOff();else ACOn();}
            if(GUILayout.Button("Low"))Low();if(GUILayout.Button("Medium"))Medium();if(GUILayout.Button("High"))High();
            if(GUILayout.Button("Stand up"))StandUp();GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();if(GUILayout.Button("Adjust position"))EditPosition();if(GUILayout.Button("Save Option A"))SaveOption();GUI.enabled=saved;if(GUILayout.Button("Restore A"))RestoreOption();GUI.enabled=true;GUILayout.EndHorizontal();
            GUILayout.Label((currentWall<0?"No wall selected":"Wall "+(currentWall+1))+" / "+(power?"ON":"OFF")+" / "+new[]{"Low","Medium","High"}[level]);
        }
        GUILayout.EndArea();
    }
    void OnDestroy(){if(mountingOutline&&mountingOutline.sharedMaterial)Destroy(mountingOutline.sharedMaterial);}
}
