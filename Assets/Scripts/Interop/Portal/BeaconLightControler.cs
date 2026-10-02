using MaidHome.Gameplay.Portal;
using UnityEngine;

public class BeaconLightControler : MonoBehaviour
{
    public GameObject LightGameObject;
    public Color SuccessColor;
    public Color FailColor;
    
    private Material _material;
    public float speed = 1;
    public PortalController _portalController;
    void Start()
    {
        _material = GetComponent<MeshRenderer>().material;
    }
    
    void Update()
    {
        if (_portalController.IsClientConnected)
            LightGameObject.transform.Rotate(new Vector3(0, Time.deltaTime * speed, 0));
        else _material.color = Color.white;
    }

    public void setSuccessColor()
    {
        if (!_portalController.IsClientConnected)return;
        _material.color = SuccessColor;
    }
    
    public void setFailColor(){
        if (!_portalController.IsClientConnected)return;
        _material.color = FailColor;
    }
}
