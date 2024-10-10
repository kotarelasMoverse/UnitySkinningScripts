using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class JointScript : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.transform.parent = this.transform;
        sphere.transform.position = this.transform.position;
        sphere.transform.localPosition = this.transform.localPosition;
        sphere.transform.localScale = new Vector3(0.03f, 0.03f, 0.03f);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
