// CropFieldSpawner.cs
using UnityEngine;

public class CropFieldSpawner : MonoBehaviour
{
    public int rows = 5, cols = 6;
    public float spacing = 2f;
    public float cylinderHeight = 1.5f;
    public Material sphereMat;
    public Material cylinderMat;

    void Start()
    {
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                Vector3 basePos = transform.position + new Vector3(c * spacing, 0, r * spacing);

                // cylinder (stalk)
                GameObject cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                cyl.transform.position = basePos + new Vector3(0, cylinderHeight * 0.5f, 0);
                cyl.transform.localScale = new Vector3(0.25f, cylinderHeight * 0.5f, 0.25f);
                if (cylinderMat) cyl.GetComponent<Renderer>().material = cylinderMat;

                // sphere (crop orb), child of cylinder
                GameObject sph = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sph.transform.SetParent(cyl.transform, false);
                sph.transform.localPosition = new Vector3(0, cylinderHeight * 0.5f + 0.3f, 0);
                sph.transform.localScale = Vector3.one * 0.35f;
                if (sphereMat) sph.GetComponent<Renderer>().material = sphereMat;

                sph.AddComponent<CropOrb>(); // starts attached (no rigidbody yet)
            }
    }
}
