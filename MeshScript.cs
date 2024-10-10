using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Unity.Collections;
using System.IO;
using System.Linq;

using System;
using UnityEngine.Animations;
using Unity.VisualScripting;

public class MeshScript : MonoBehaviour
{
    GameObject newSkeleton;
    GameObject[] newSkeleton2;
    GameObject[] newSkeleton3;
    Transform[] jointsTransforms;
    GameObject[] jointSpheres;
    GameObject[] jointSpheres2;
    public struct JointInfoAndOffset {
        public string jointName;
        public Vector3 vectorFromParent;
    }

    public class HierarchyAndOffsets {
        private JointInfoAndOffset data;
        private string jointName;
        private Vector3 vectorFromParent;
        private LinkedList<HierarchyAndOffsets> children;
        private Dictionary<Transform, Vector3> vectorsToChildren;

        public HierarchyAndOffsets(Transform root, Vector3 vectorFromParent) {

            this.data = new JointInfoAndOffset{jointName = root.name, vectorFromParent = vectorFromParent};
            this.children = new LinkedList<HierarchyAndOffsets>();
            this.vectorsToChildren = new Dictionary<Transform, Vector3>();
            this.jointName = root.name;
            this.vectorFromParent = vectorFromParent;

            foreach (Transform child in root) {
                this.children.AddFirst(new HierarchyAndOffsets(child, child.position - root.position)); // Offset is equal to child.localPosition
                this.vectorsToChildren[child] = child.position - root.position;
            }
        }
    }

    public class Joint {
        public string name;
        public int id;
        public Transform transf;
        public List<Tuple<int, Joint, Vector3>> vectorsToChildren;

        public Joint() {

        }
    }

    public struct VertexSkinningWeigts {
        public int[] bonesIds;
        public float[] weights;
    }

    // class JointOffsetEqualityComparer : IEqualityComparer<JointOffset>
    // {
    //     public bool Equals(JointOffset jo1, JointOffset jo2)
    //     {
    //         if (ReferenceEquals(jo1, jo2))
    //             return true;

    //         if (jo2 is null || jo1 is null)
    //             return false;

    //         return jo1.Height == b2.Height
    //             && jo1.Length == b2.Length
    //             && jo1.Width == b2.Width;
    //     }

    //     public int GetHashCode(JointOffset jo) => jo.jointName ^ jo.offsetFromParent;
    // }

    // Start is called before the first frame update
    public GameObject moverseModel;
    public GameObject cloneModel;
    public GameObject meshObject;
    public Mesh mesh;
    private int numVertices;
    private ComputeBuffer _vertexBuffer;
    public ComputeShader computeShader;
    private float[] vertexBufferContent;
    private Vector3[] vertices;
    // int kernel;

    void Start()
    {
        moverseModel = GameObject.Find("Free-Dance-Movements");
        cloneModel = GameObject.Find("Clone");
        meshObject = GameObject.Find("standaloneMesh");
        mesh = meshObject.GetComponent<MeshFilter>().sharedMesh;
        // Matrix4x4 meshObjectTransfMatrix = meshObject.GetComponent<MeshRenderer>().localToWorldMatrix;
        numVertices = mesh.vertexCount;
        Debug.Log("Vertex number " + numVertices);        

        // Set the model to T-pose
        Transform skeletonTransform = cloneModel.transform.GetChild(0);
        Transform root = skeletonTransform.GetChild(0);
        SetModelToTPose(root);

        // Extract vertices position in T-pose, the position are in local coordinate system with center the center of the mesh (average of all the vertices positions and position of the mesh object)
        SkinnedMeshRenderer rend = root.GetComponentInParent<SkinnedMeshRenderer>();
        Mesh cloneMesh = rend.sharedMesh;
        Vector3[] cloneVertices = cloneMesh.vertices;
        Debug.Log("Clone mesh vertex number " + cloneVertices.Length);

        // Get center of mesh
        Vector3 cloneMeshCenter = new Vector3();
        for (int i=0; i<cloneVertices.Length; i++) {
            cloneMeshCenter += rend.localToWorldMatrix.MultiplyPoint(cloneVertices[i]);
        }
        cloneMeshCenter = cloneMeshCenter / cloneVertices.Length;
        Debug.Log("Clone mesh center in world coordinates" + cloneMeshCenter);
        Debug.Log("Root position " + root.position);

        // Get the positions of the vertices in T-pose in the world coordinate system
        Vector3[] cloneVerticesWorldCoord = new Vector3[cloneVertices.Length];
        Matrix4x4 transfMatrix = rend.localToWorldMatrix;
        for (int i=0; i<cloneVertices.Length; i++) {
            cloneVerticesWorldCoord[i] = transfMatrix.MultiplyPoint(cloneVertices[i]);
        }

        // Get the positions of the vertices in T-pose in the root joint coordinate system
        Vector3[] cloneVerticesRootCoord = new Vector3[cloneVerticesWorldCoord.Length];
        for (int i=0; i<cloneVerticesWorldCoord.Length; i++) {
            cloneVerticesRootCoord[i] = cloneVerticesWorldCoord[i] - root.position;
        }

        // Get joints/bones offsets in T-pose
        HierarchyAndOffsets hierarchyAndOffsets = new HierarchyAndOffsets(root, new Vector3(0,0,0));

        // Get list of joints/bones in T-pose, with the index of the list corresponding to the id of the bone
        Transform[] cloneBones = rend.bones;

        // Get bone weights for the vertices 
        var cloneBoneWeights = cloneMesh.GetAllBoneWeights();
        var cloneBonesPerVertex = cloneMesh.GetBonesPerVertex();
        Dictionary<int, VertexSkinningWeigts> vertexSkinningInfo = GetVertexSkinningInfo(cloneBoneWeights, cloneBonesPerVertex);


        // Try to build a new mesh from the extracted data and render it on screen
        // GameObject newMesh = new GameObject("newMesh", typeof(MeshFilter), typeof(MeshRenderer));
        // newMesh.GetComponent<MeshFilter>().mesh = Resources.Load("moverse_mesh") as Mesh;
        // newMesh.GetComponent<MeshFilter>().mesh.vertices = cloneVertices.Clone() as Vector3[]; // these vertices will always have as center the (0,0,0) by default
        // Vector3[] vert = newMesh.GetComponent<MeshFilter>().mesh.vertices;
        // newMesh.transform.position = new Vector3(10,0,0);
        // Vector3 vSum = new Vector3();
        // for (int i=0; i<vert.Length; i++) {
        //     vSum += vert[i];
        // }
        // print(vSum/vert.Length);
        // newMesh.GetComponent<MeshRenderer>().material = Resources.Load("material 3") as Material;

        Vector3 origin = new Vector3(0,0,0);
        Vector3 p = new Vector3(1,1,0);
        Vector3 v = new Vector3(2,1,0);
        Vector3 v_ = Rotations.Rotated(v, Quaternion.Euler(90, 0, 0)*Quaternion.Euler(0, 0, 90), p);
        print("original " + v + " rotated " + v_);
        
        
        //Possible paths for root joint to "leaf" joints
        List<int[]> paths = new List<int[]>();
        paths.Add(new int[] {0, 1, 4, 7, 10});
        paths.Add(new int[] {0, 2, 5, 8, 11});
        paths.Add(new int[] {0, 3, 6, 9, 12, 15});
        paths.Add(new int[] {0, 3, 6, 9, 12, 13, 16, 18, 20, 22});
        paths.Add(new int[] {0, 3, 6, 9, 12, 14, 17, 19, 21, 23});

        // Hypothetical animation parameters // These animation parameters ar wrt to the world coordinate system
        Dictionary<int, Vector3> animation = new Dictionary<int, Vector3>(); 
        animation[1] = new Vector3(45, 0, 0); // Rotate L Hip around x by 45 degrees
        animation[4] = new Vector3(45, 0, 0); // Rotate L Knee around x by 60 degrees
        animation[7] = new Vector3(45, 0, 0); // Rotate L Ankle around x by 45 degrees
        animation[17] = new Vector3(0, 0, -30); // Rotate R shoulder around x by 90 degrees
        animation[19] = new Vector3(0, -100, 0); // Rotate R elbow around y by 90 degrees


        // For each bone/joint gather the matrices that transform it to the animated location, or also calculate the total matrix for that transformation
        Dictionary<int, List<Matrix4x4>> boneTransformations = new Dictionary<int, List<Matrix4x4>>();
        for (int i=0; i<cloneBones.Length; i++) {
            boneTransformations[i] = new List<Matrix4x4>();
        }
        foreach (int boneId in animation.Keys) {
            // get the transformation matrix of the cuurent bone
            Matrix4x4 currentBoneTransformationMatrix = Matrix4x4.Rotate(Quaternion.Euler(animation[boneId]));
            // get path that contains this bone
            int[] wholeBonePath = paths.Find(x => x.Contains(boneId));
            int[] bonePath = new int[wholeBonePath.Length - Array.IndexOf(wholeBonePath, boneId)];
            Array.Copy(wholeBonePath, Array.IndexOf(wholeBonePath, boneId), bonePath, 0, bonePath.Length);
            // for each child bone in the path, add the transformation matrix of this bone to the matrices of the child bone
            foreach (int childBoneId in bonePath) {
                boneTransformations[childBoneId].Add(currentBoneTransformationMatrix);
            }
        }

        print(1);

        // Kinematics, transform the skeleton to the new position
        Transform[] cloneBonesCopy = cloneBones.Clone() as Transform[];
        for (int boneId=0; boneId<boneTransformations.Count; boneId++) {

            if (!animation.Keys.Contains(boneId)) {
                continue;
            }

            Matrix4x4 currentBoneTransformationMatrix = Matrix4x4.Rotate(Quaternion.Euler(animation[boneId]));

            // Get all the children of this bone
            HashSet<int> childrenBones = new HashSet<int>();
            List<int[]> pathsWithBone = paths.FindAll(x => x.Contains(boneId));
            foreach (int[] path in paths.FindAll(x => x.Contains(boneId))) {
                int[] bonePath = new int[path.Length - Array.IndexOf(path, boneId) - 1];
                Array.Copy(path, Array.IndexOf(path, boneId) + 1, bonePath, 0, bonePath.Length);
                childrenBones.UnionWith(bonePath);
            }

            foreach(int childBoneId in childrenBones) {
                // cloneBonesCopy[childBoneId].localPosition = currentBoneTransformationMatrix.MultiplyPoint(cloneBonesCopy[childBoneId].localPosition - cloneBonesCopy[boneId].localPosition) + cloneBonesCopy[boneId].localPosition;
                // cloneBones[childBoneId].localPosition = Rotations.Rotated(cloneBones[childBoneId].localPosition, Quaternion.Euler(animation[1]), cloneBones[boneId].localPosition);
            }
        }

        // Try to rotate the vertices of the clone model around the root
        Vector3[] newRootCoord = new Vector3[cloneVerticesRootCoord.Length];
        Vector3[] newWorldCoord = new Vector3[cloneVerticesRootCoord.Length];
        Vector3[] newLocalCoord = new Vector3[cloneVerticesRootCoord.Length];
        for (int vid=0; vid<cloneVerticesRootCoord.Length; vid++) {
            newRootCoord[vid] = Matrix4x4.Rotate(Quaternion.Euler(90, 0, 0)).MultiplyPoint(cloneVerticesRootCoord[vid]);
            newWorldCoord[vid] = newRootCoord[vid] + root.position;
            newLocalCoord[vid] = transfMatrix.inverse.MultiplyPoint(newWorldCoord[vid]);
        }
        // cloneMesh.vertices = newLocalCoord;



        // Create a new skeleton
        newSkeleton = buildSkeleton(root, null, false);
        newSkeleton2 = buildSkeleton2(cloneBones);
        newSkeleton3 = buildSkeleton2(cloneBones);

        // Setup spheres that follow the joints of newSkeleton for visualization purposes
        jointsTransforms = newSkeleton.GetComponentsInChildren<Transform>();
        jointSpheres = new GameObject[jointsTransforms.Length];
        for (int j=0; j<jointSpheres.Length; j++) {
            jointSpheres[j] = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        }
        // Setup spheres that follow the joints of newSkeleton2 for visualization purposes
        jointSpheres2 = new GameObject[newSkeleton2.Length];
        for (int j=0; j<jointSpheres2.Length; j++) {
            jointSpheres2[j] = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        }


        // Create a new mesh at the position of newSkeleton2,
        GameObject newSkeleton2Mesh = CreateNewMeshAtPosition(newSkeleton2[0].transform, cloneVerticesRootCoord); // Pass the vertices position wrt the root bone (pelvis), so the vertices of the new mesh are also in the coordinate system of the root bone
        newSkeleton2Mesh.name = "skeleton2Mesh";
        Debug.Log($"Mesh object local pos {newSkeleton2Mesh.transform.localPosition}");
        Debug.Log($"Mesh object pos {newSkeleton2Mesh.transform.position}");

        // Create a new mesh at the position of newSkeleton2,
        GameObject newSkeleton3Mesh = CreateNewMeshAtPosition(newSkeleton3[0].transform, cloneVerticesRootCoord); // Pass the vertices position wrt the root bone (pelvis), so the vertices of the new mesh are also in the coordinate system of the root bone
        newSkeleton3Mesh.name = "skeleton3Mesh";
        Debug.Log($"Mesh object local pos {newSkeleton3Mesh.transform.localPosition}");
        Debug.Log($"Mesh object pos {newSkeleton3Mesh.transform.position}");
    
        // Create a new mesh at the position of newSkeleton
        GameObject newSkeletonMesh = CreateNewMeshAtPosition(newSkeleton.transform, cloneVerticesRootCoord);
        newSkeletonMesh.name = "skeletonMesh";

        // Get the offsets between the vertices and the joints at T-Pose for skeleton2
        Dictionary<int, Dictionary<int, Vector3>> newSkeleton2VerticesAndJointsOffsets = GetVerticesAndJointsOffsetsInTPose(vertexSkinningInfo, cloneVerticesWorldCoord, newSkeleton2);
        Dictionary<int, Dictionary<int, Vector3>> newSkeleton3VerticesAndJointsOffsets = GetVerticesAndJointsOffsetsInTPose(vertexSkinningInfo, cloneVerticesWorldCoord, newSkeleton3);

        // Animate new skeleton
        AnimateSkeleton(newSkeleton.transform, animation, cloneBones);
        // AnimateSkeleton2(newSkeleton2, animation, cloneBones, paths);

        // OR Animate the skeleton and get all the rotations that the joints were subject to
        List<(Quaternion, Vector3)>[] jointsRotations = AnimateSkeleton2WithRotationsAccumulation(newSkeleton2, animation, cloneBones, paths);
        List<(Quaternion, Vector3)>[] jointsRotations3 = AnimateSkeleton2WithRotationsAccumulation(newSkeleton3, animation, cloneBones, paths);


        // Simmple Skinning of the mesh of the newSkeleton2
        // Vector3[] simpleSkinnedNewSkeleton2MeshVertices = SimpleSkinningFromJointsAccumulatedRotations(newSkeleton2Mesh.GetComponent<MeshFilter>().mesh.vertices, newSkeleton2[0].transform.position, vertexSkinningInfo, jointsRotations);
        // Mesh m = Resources.Load("moverse_mesh") as Mesh;
        // m.vertices = simpleSkinnedNewSkeleton2MeshVertices;
        // newSkeleton2Mesh.GetComponent<MeshFilter>().mesh = m;

        // Simple skinning of the mesh of newSkeleton2 using offsets
        Vector3[] simpleSkinnedNewSkeleton2MeshVertices = SimpleSkinningFromOffsets(newSkeleton2, newSkeleton2VerticesAndJointsOffsets);
        Mesh m = Resources.Load("moverse_mesh1") as Mesh;
        Mesh meshInstance = Instantiate(m);
        meshInstance.vertices = simpleSkinnedNewSkeleton2MeshVertices;
        newSkeleton2Mesh.GetComponent<MeshFilter>().mesh = meshInstance;

        // Linear Blend Skinning of the mesh of newSkeleton3 using offsets
        Vector3[] linearBlendSkinnedNewSkeleton3MeshVertices = LinearBlendSkinningFromOffsets(newSkeleton3, newSkeleton3VerticesAndJointsOffsets, vertexSkinningInfo);
        Mesh n = Resources.Load("moverse_mesh2") as Mesh;
        n.vertices = linearBlendSkinnedNewSkeleton3MeshVertices;
        newSkeleton3Mesh.GetComponent<MeshFilter>().mesh = n;

        // for (int vid=0; vid<cloneVertices.Length; vid++) {

        //     Vector3 vertexPos = new Vector3();

        //     for (int b=0; b<vertexSkinningInfo[vid].bonesIds.Length; b++) {

        //         Vector3 totalTransformationBasedOnThisBone = cloneVertices[vid];
        //         for (int j=0; j<boneTransformations[b].Count; j++) {
        //             totalTransformationBasedOnThisBone = boneTransformations[b][j].MultiplyPoint(totalTransformationBasedOnThisBone);
        //         }

        //         vertexPos += vertexSkinningInfo[vid].weights[b] * totalTransformationBasedOnThisBone;
        //     }
        //     cloneVertices[vid] = vertexPos;
        //     // if (vertexSkinningInfo[vid].bonesIds.Contains(1)) {

        //     //     // cloneVertices[vid] = boneTransformations[1][0].MultiplyPoint(cloneVertices[vid]);
        //     //     cloneVertices[vid] = boneTransformations[1][0].MultiplyPoint(cloneVertices[vid] - root.position) + root.position;
        //     // }
        // }

        // rend.sharedMesh.vertices = cloneVertices.Clone() as Vector3[];
        

        


        // Vector3[] standaloneMeshVertices = meshObject.GetComponent<MeshFilter>().sharedMesh.vertices;
        // Vector3 centroid = Vector3.zero;
        // foreach (Vector3 vertex in standaloneMeshVertices)
        // {
        //     centroid += vertex;
        // }
        // centroid /= standaloneMeshVertices.Length;
        // print("Centroid of standalone mesh vertices " + centroid);
        // print("Transformed Centroid of standalone mesh vertices " + meshObject.GetComponent<MeshRenderer>().localToWorldMatrix.MultiplyPoint(centroid));

        // centroid = Vector3.zero;
        // foreach (Vector3 vertex in cloneVertices)
        // {
        //     centroid += vertex;
        // }
        // centroid /= cloneVertices.Length;
        // print("Centroid of clone model mesh vertices " + centroid);
        // print("Transformed Centroid of clone model mesh vertices " + rend.localToWorldMatrix.MultiplyPoint(centroid));


        // Vector3[] moverseMeshVertices = GameObject.Find("moverse_mesh").GetComponent<MeshFilter>().sharedMesh.vertices;
        // centroid = Vector3.zero;
        // foreach (Vector3 vertex in moverseMeshVertices)
        // {
        //     centroid += vertex;
        // }
        // centroid /= moverseMeshVertices.Length;
        // print("Centroid of moverse mesh vertices " + centroid);
        // print("Transformed Centroid of moverse mesh vertices " + GameObject.Find("moverse_mesh").GetComponent<MeshRenderer>().localToWorldMatrix.MultiplyPoint(centroid));

        // var bonesPerVertex = mesh.GetBonesPerVertex();
        // print(bonesPerVertex.Length);

        // var boneWeights = mesh.GetAllBoneWeights();
        // print(boneWeights.Length);

        // Keep track of where we are in the array of BoneWeights, as we iterate over the vertices
        // var boneWeightIndex = 0;

        // // Iterate over the vertices
        // for (var vertIndex = 0; vertIndex < numVertices; vertIndex++)
        // {
        //     var totalWeight = 0f;
        //     var numberOfBonesForThisVertex = bonesPerVertex[vertIndex];
        //     // Debug.Log("This vertex has " + numberOfBonesForThisVertex + " bone influences");

        //     // For each vertex, iterate over its BoneWeights
        //     for (var i = 0; i < numberOfBonesForThisVertex; i++)
        //     {
        //         var currentBoneWeight = boneWeights[boneWeightIndex];
        //         totalWeight += currentBoneWeight.weight;
        //         if (i > 0)
        //         {
        //             Debug.Assert(boneWeights[boneWeightIndex - 1].weight >= currentBoneWeight.weight);
        //         }
        //         boneWeightIndex++;
        //     }
        //     Debug.Assert(Mathf.Approximately(1f, totalWeight));
        // }

        // CopyTransforms(moverseModel.transform, cloneModel.transform);
        // SkinnedMeshRenderer meshRenderer = GameObject.Find("rig_MCUnity").transform.GetChild(0).GetComponent<SkinnedMeshRenderer>();
        // Mesh skinnedMesh = meshRenderer.sharedMesh;
        // // var weitghs = skinnedMesh.GetAllBoneWeights();
        // var bonesPerVertex = skinnedMesh.GetBonesPerVertex();

        // BoneWeight1[] weights = new BoneWeight1[113548];
        // var weightsArray = new NativeArray<BoneWeight1>(weights, Allocator.Temp);
        // print(weightsArray.Length);
        // print(bonesPerVertex.Length);

        // skinnedMesh.SetBoneWeights(bonesPerVertex, weightsArray);
        
        // print(weitghs.Length);
        // print(weitghs[0]);
        // print(bonesPerVertex.Length);

        // for (int i=0; i<weitghs.Length; i++) {

        //     weitghs[i].weight = 0;
        // }
    }

    // Update is called once per frame
    void Update()
    {   
        // Make the spheres follow the joints of newSkeleton
        for (int j=0; j<jointSpheres.Length; j++) {
            jointSpheres[j].transform.localScale = new Vector3(0.03f, 0.03f, 0.03f);
            jointSpheres[j].transform.position = jointsTransforms[j].position;
        }

        // Make the spheres follow the joints of newSkeleton2
        for (int j=0; j<jointSpheres2.Length; j++) {
            jointSpheres2[j].transform.localScale = new Vector3(0.03f, 0.03f, 0.03f);
            jointSpheres2[j].transform.position = newSkeleton2[j].transform.position;
        }
        
        // CopyTransforms(moverseModel.transform, cloneModel.transform);
        // cloneModel.transform.position += new Vector3(2, 0, 0);

        // // Move mesh randomly
        // MovemeshRandomly();
        // Vector3[] vert = cloneModel.transform.GetChild(0).GetComponent<SkinnedMeshRenderer>().sharedMesh.vertices;
        // Debug.Log(vert[0]);
        
    }

    // Copy transform from source model to target model (Clone game object)
    public void CopyTransforms(Transform source, Transform target) {

        Transform[] ts = source.GetComponentsInChildren<Transform>();
        Transform[] tt = target.GetComponentsInChildren<Transform>();
        
        if (ts.Length != tt.Length) {
            Debug.Log("Transform arrays not equal");
            return;
        }

        // for (int i=0; i<ts.Length; i++) {
        //     tt[i] = ts[i];
        // }
        
        for (int i=0; i<source.childCount; i++) {
            
            Transform sourceChild = source.GetChild(i);
            Transform targetChild = target.GetChild(i);

            // print(sourceChild.name);
            // print(targetChild.name);
            
            targetChild.transform.position = sourceChild.transform.position;
            targetChild.transform.rotation = sourceChild.transform.rotation;

            // Find corresponding target child
            CopyTransforms(sourceChild, targetChild);
        }
    }

    // Move the vertices of the standalone mesh
    public void MovemeshRandomly() {

        vertices = mesh.vertices;

        _vertexBuffer = new ComputeBuffer(numVertices*3, sizeof(float));
        vertexBufferContent = new float[numVertices*3];
        for (int i=0; i<numVertices*3; i=i+3) {
            vertexBufferContent[i + 0] = vertices[i/3][0];
            vertexBufferContent[i + 1] = vertices[i/3][1];
            vertexBufferContent[i + 2] = vertices[i/3][2];
        }
        _vertexBuffer.SetData(vertexBufferContent);

        int kernel = computeShader.FindKernel("MoveVerticesRandomly");
        computeShader.SetBuffer(kernel, "_VertexBuffer", _vertexBuffer);
        computeShader.SetInt("_NumVertices", numVertices);
        float randomValue = UnityEngine.Random.Range(-1f, 1f);
        computeShader.SetFloat("_RandomFloat", randomValue);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(numVertices / 1.0f), 1, 1);

        _vertexBuffer.GetData(vertexBufferContent);
        for (int i=0; i<numVertices*2; i=i+3) {
            vertices[i/3][0] = vertexBufferContent[i + 0];
            vertices[i/3][1] = vertexBufferContent[i + 1];
            vertices[i/3][2] = vertexBufferContent[i + 2];
        }
        mesh.vertices = vertices;
    }

    public void SetModelToTPose(Transform root) {

        // root.localRotation = Quaternion.identity;

        foreach(Transform child in root) {
            child.localRotation = Quaternion.identity;

            SetModelToTPose(child);
        }
    }

    // Return a Dictionary with keys the id of a vertex and values the bones that this vertex is influenced from and the influence weights.
    public Dictionary<int, VertexSkinningWeigts> GetVertexSkinningInfo(NativeArray<BoneWeight1> cloneBoneWeights, NativeArray<byte> cloneBonesPerVertex) {

        Dictionary<int, VertexSkinningWeigts> ret = new Dictionary<int, VertexSkinningWeigts>();

        var boneWeightIndex = 0;

        // Iterate over the vertices
        for (var vertIndex = 0; vertIndex < cloneBonesPerVertex.Length; vertIndex++)
        {
            var totalWeight = 0f;
            var numberOfBonesForThisVertex = cloneBonesPerVertex[vertIndex];
            ret.Add(vertIndex, new VertexSkinningWeigts{bonesIds=new int[numberOfBonesForThisVertex], weights=new float[numberOfBonesForThisVertex]});
            // Debug.Log("This vertex has " + numberOfBonesForThisVertex + " bone influences");

            // For each vertex, iterate over its BoneWeights
            for (var i = 0; i < numberOfBonesForThisVertex; i++)
            {   
                var currentBoneWeight = cloneBoneWeights[boneWeightIndex];

                ret[vertIndex].bonesIds[i] = currentBoneWeight.boneIndex;
                ret[vertIndex].weights[i] = currentBoneWeight.weight;

                
                totalWeight += currentBoneWeight.weight;
                if (i > 0)
                {
                    Debug.Assert(cloneBoneWeights[boneWeightIndex - 1].weight >= currentBoneWeight.weight);
                }
                boneWeightIndex++;
            }
            Debug.Assert(Mathf.Approximately(1f, totalWeight));
        }

        return ret;
    }

    public GameObject buildSkeleton(Transform source, Transform parent, bool createSpheresAtJoints) {
        GameObject node = new GameObject();

        if (parent is not null) {
            node.transform.parent = parent;
        }
        node.transform.localRotation = Quaternion.identity;
        
        if (parent is null) {
            node.transform.position = Vector3.zero;
            node.transform.localPosition = Vector3.zero;
        }
        else {
            node.transform.localPosition = source.localPosition;
        }

        node.name = source.name;
        
        if (createSpheresAtJoints) {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.localScale = new Vector3(0.03f, 0.03f, 0.03f);
            sphere.transform.parent = node.transform;
            sphere.transform.position = node.transform.position;
            sphere.transform.localPosition = Vector3.zero;
        }

        foreach (Transform sourceChild in source) {
            GameObject child = buildSkeleton(sourceChild, node.transform, createSpheresAtJoints);
        }

        return node;
    }

    public GameObject[] buildSkeleton2(Transform[] bones) {
        
        // THis version just copies the transformation from the renderer.bones list to the new game objects
        GameObject[] skeleton = new GameObject[bones.Length];

        for (int rj=0; rj<bones.Length; rj++) {
            skeleton[rj] = new GameObject();
            skeleton[rj].transform.position = bones[rj].position;
            skeleton[rj].transform.localRotation = Quaternion.identity;
            skeleton[rj].transform.rotation = Quaternion.identity;
            skeleton[rj].name = bones[rj].name;
        }

        return skeleton;

        // The following implementation did not add the joints in the correct order to the list
        // Transform[] rootJoints = source.GetComponentsInChildren<Transform>();

        // GameObject[] skeleton = new GameObject[rootJoints.Length];

        // for (int rj=0; rj<rootJoints.Length; rj++) {
        //     skeleton[rj] = new GameObject();
        //     skeleton[rj].transform.position = rootJoints[rj].position;
        //     skeleton[rj].transform.localRotation = Quaternion.identity;
        //     skeleton[rj].transform.rotation = Quaternion.identity;
        //     skeleton[rj].name = rootJoints[rj].name;
        // }

        // return skeleton;
    }

    public void AnimateSkeleton(Transform node, Dictionary<int, Vector3> animation, Transform[] skeletonBones) {

        // Get the corresponding bone id
        int boneId = Array.FindIndex(skeletonBones, x => x.name == node.name);

        // If there is an animation param for this bone
        if (animation.Keys.ToList().Contains(boneId)) {

            // Rotate it
            node.Rotate(animation[boneId]);
        }

        foreach (Transform childNode in node) {

            AnimateSkeleton(childNode, animation, skeletonBones);

            // var animationIndex = animation.Keys.ToList().FindIndex(x => cloneBones[x].name == node.name);
            // if (animationIndex == -1) continue;
        }

    }

    public void AnimateSkeleton2(GameObject[] skeleton, Dictionary<int, Vector3> animation, Transform[] skeletonBones, List<int[]> allPaths) {

        for (int j=0; j<skeleton.Length; j++) {
            // Get the corresponding bone id
            int boneId = Array.FindIndex(skeletonBones, x => x.name == skeleton[j].name);

            // If there is an animation param for this bone
            if (animation.Keys.ToList().Contains(boneId)) {

                // Rotate it
                // node.Rotate(animation[boneId]);

                skeleton[boneId].transform.localRotation = Quaternion.Euler(animation[boneId]);
                // Get the joint ids of its children
                HashSet<int> childrenJoints = FindChildrenJointsOf(boneId, allPaths);

                // Rotate the all the children joints around this joint by as many degrees as the animation parameter dictates
                foreach (int cj in childrenJoints) {
                    // Here we use world coordinates
                    skeleton[cj].transform.position = Rotations.Rotated(skeleton[cj].transform.position, Quaternion.Euler(animation[boneId]), skeleton[boneId].transform.position);
                }

                // // Get the transforms of its children
                // Transform[] recursiveChildrenTransforms = node.GetComponentsInChildren<Transform>();

                // foreach (Transform child in recursiveChildrenTransforms) {
                    
                //     child.position = Rotations.Rotated(child.position, Quaternion.Euler(new Vector3(45, 0, 0)), node.transform.position);
                //     Debug.Log($"Rotated {child.name} by 45 deg");
                // }
            }
        }

    }

    public List<(Quaternion, Vector3)>[] AnimateSkeleton2WithRotationsAccumulation(GameObject[] skeleton, Dictionary<int, Vector3> animation, Transform[] skeletonBones, List<int[]> allPaths) {

        List<(Quaternion, Vector3)>[] jointsRotations = new List<(Quaternion, Vector3)>[skeleton.Length];
        for (int j=0; j<skeleton.Length; j++) {
            // Initialize the lists of the rotation tuples
            jointsRotations[j] = new List<(Quaternion, Vector3)>();
        }

        for (int j=0; j<skeleton.Length; j++) {
            // Get the corresponding bone id
            int boneId = Array.FindIndex(skeletonBones, x => x.name == skeleton[j].name);

            // If there is an animation param for this bone
            if (animation.Keys.ToList().Contains(boneId)) {

                // Rotate it
                // node.Rotate(animation[boneId]);

                // Add an entry to the rotaions list for this joint
                jointsRotations[boneId].Add((Quaternion.Euler(animation[boneId]), skeleton[boneId].transform.position));

                // skeleton[boneId].transform.localRotation = skeleton[boneId].transform.localRotation * Quaternion.Euler(animation[boneId]);
                skeleton[boneId].transform.transform.Rotate(animation[boneId]);
                // Get the joint ids of its children
                HashSet<int> childrenJoints = FindChildrenJointsOf(boneId, allPaths);

                // Rotate the all the children joints around this joint by as many degrees as the animation parameter dictates
                foreach (int cj in childrenJoints) {
                    // Here we use world coordinates

                    // Rotate the child joint around the parent joint
                    // skeleton[cj].transform.Rotate(animation[boneId]);
                    // skeleton[cj].transform.position = Rotations.Rotated(skeleton[cj].transform.position, Quaternion.Euler(animation[boneId]), skeleton[boneId].transform.position);
                    skeleton[cj].transform.RotateAround(skeleton[boneId].transform.position, skeleton[boneId].transform.right, animation[boneId][0]);
                    skeleton[cj].transform.RotateAround(skeleton[boneId].transform.position, skeleton[boneId].transform.up, animation[boneId][1]);
                    skeleton[cj].transform.RotateAround(skeleton[boneId].transform.position, skeleton[boneId].transform.forward, animation[boneId][2]);
                    // skeleton[cj].transform.localRotation = skeleton[cj].transform.localRotation * Quaternion.Euler(animation[boneId]);
                    

                    // Add an entry to the rotaions list for the child joint
                    jointsRotations[cj].Add((Quaternion.Euler(animation[boneId]), skeleton[boneId].transform.position));
                    // jointsRotations[cj].Add((skeleton[cj].transform.localRotation, Vector3.zero));
                }

                // // Get the transforms of its children
                // Transform[] recursiveChildrenTransforms = node.GetComponentsInChildren<Transform>();

                // foreach (Transform child in recursiveChildrenTransforms) {
                    
                //     child.position = Rotations.Rotated(child.position, Quaternion.Euler(new Vector3(45, 0, 0)), node.transform.position);
                //     Debug.Log($"Rotated {child.name} by 45 deg");
                // }
            }
        }

        return jointsRotations;
    }

    public HashSet<int> FindChildrenJointsOf(int startBone, List<int[]> allPaths) {

        HashSet<int> childrenBones = new HashSet<int>();
        List<int[]> pathsWithBone = allPaths.FindAll(x => x.Contains(startBone));
        foreach (int[] path in pathsWithBone) {
            int[] bonePath = new int[path.Length - Array.IndexOf(path, startBone) - 1];
            Array.Copy(path, Array.IndexOf(path, startBone) + 1, bonePath, 0, bonePath.Length);
            childrenBones.UnionWith(bonePath);
        }

        return childrenBones;
    }

    public GameObject CreateNewMeshAtPosition(Transform position, Vector3[] cloneVertices) {
        Debug.Log($"Creating new mesh at {position.position}");


        // GameObject newMeshParent = new GameObject("newMeshParent");
        // newMeshParent.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        // newMeshParent.transform.position = position.position;

        GameObject newMesh = new GameObject("newMesh");
        
        newMesh.transform.position = position.position;
        // newMesh.transform.SetParent(newMeshParent.transform);
        newMesh.AddComponent<MeshFilter>();
        newMesh.AddComponent<MeshRenderer>();
        Mesh m = Resources.Load("moverse_mesh") as Mesh;
        m.vertices = cloneVertices;
        newMesh.GetComponent<MeshFilter>().mesh = m;
        
        // newMesh.GetComponent<MeshFilter>().mesh.vertices = cloneVertices.Clone() as Vector3[]; // these vertices will always have as center the (0,0,0) by default
        // 

        // newMesh.AddComponent<SkinnedMeshRenderer>();
        // newMesh.GetComponent<SkinnedMeshRenderer>().sharedMesh = Resources.Load("moverse_mesh") as Mesh;
        // Transform[] bones = new Transform[4];
        // for (int i=0; i<bones.Length; i++) {
        //     bones[i] = skeleton[i].transform;
        // }
        // newMesh.GetComponent<SkinnedMeshRenderer>().bones = bones;
        // newMesh.GetComponent<SkinnedMeshRenderer>().rootBone = bones[0];
        

        Vector3[] vert = newMesh.GetComponent<MeshFilter>().mesh.vertices;
        // Vector3[] vert = newMesh.GetComponent<SkinnedMeshRenderer>().sharedMesh.vertices;
        Vector3 vSum = new Vector3();
        for (int i=0; i<vert.Length; i++) {
            vSum += vert[i];
        }
        Matrix4x4 transfMatrix = newMesh.GetComponent<MeshRenderer>().localToWorldMatrix;
        // Matrix4x4 transfMatrix = newMesh.GetComponent<SkinnedMeshRenderer>().worldToLocalMatrix;
        Debug.Log($"New mesh center coords in local coord system {vSum/vert.Length}");
        Debug.Log($"New mesh center coords in world coord system {transfMatrix.MultiplyPoint(vSum/vert.Length)}");

        newMesh.GetComponent<MeshRenderer>().material = Resources.Load("material 3") as Material;
        // newMesh.GetComponent<SkinnedMeshRenderer>().material = Resources.Load("material 3") as Material;

        return newMesh;
    }

    // SImple Skinning : use only one bone per vertex
    public Vector3[] SimpleSkinningFromJointsAccumulatedRotations(Vector3[] vertices, Vector3 rootJointPosition, Dictionary<int, VertexSkinningWeigts> vertexSkinningInfo, List<(Quaternion, Vector3)>[] jointsRotations) {
        
        Vector3[] newVerticesPositions = new Vector3[vertices.Length];
        // For each vertex
        for (int vid=0; vid<vertices.Length; vid++) {

            // Get the id of the bone that influences this vertex, with the largest weight (first bone)
            int influenceBoneId = vertexSkinningInfo[vid].bonesIds[0];

            // Get the rotations that this bone was subject to
            List<(Quaternion, Vector3)> influenceBoneRotations = jointsRotations[influenceBoneId];

            // Initialize the new vertex possition as the current vertex position
            // Vector3 newVertexPos = vertices[vid];
            // foreach (var rot in influenceBoneRotations) {
            //     // Rotate the vertex sequencially, 
            //     newVertexPos = Rotations.Rotated(newVertexPos, rot.Item1, rot.Item2 - rootJointPosition); // Thepivot is transformed to root joint coordinates because the vertices are in root joint coordinates
            // }

            newVerticesPositions[vid] = CalculateVertexPositionFromInfluenceBoneRotations(vertices[vid], influenceBoneRotations, rootJointPosition);
        }

        return newVerticesPositions;
    }

    public Vector3 CalculateVertexPositionFromInfluenceBoneRotations(Vector3 vertexStartPos, List<(Quaternion, Vector3)> influenceBoneRotations, Vector3 rootJointPosition) {

        Vector3 newVertexPos = vertexStartPos;
        foreach (var rot in influenceBoneRotations) {
            // Rotate the vertex sequencially, 
            newVertexPos = Rotations.Rotated(newVertexPos, rot.Item1, rot.Item2 - rootJointPosition); // Thepivot is transformed to root joint coordinates because the vertices are in root joint coordinates
        }

        return newVertexPos;
    }

    public Vector3[] SimpleSkinningFromOffsets(GameObject[] skeleton, Dictionary<int, Dictionary<int, Vector3>> verticesAndJointsOffsetsInTPose) {
        
        Vector3[] newVerticesPositions = new Vector3[verticesAndJointsOffsetsInTPose.Count];

        // For each vertex
        for (int vid=0; vid<newVerticesPositions.Length; vid++) {

            // Get the id of the bone that influences this vertex, with the largest weight (first bone)
            int influenceBoneId = verticesAndJointsOffsetsInTPose[vid].Keys.ToList()[0];

            // Get the rotations that this bone was subject to

            // Initialize the new vertex possition as the current vertex position
            // Vector3 newVertexPos = vertices[vid];
            // foreach (var rot in influenceBoneRotations) {
            //     // Rotate the vertex sequencially, 
            //     newVertexPos = Rotations.Rotated(newVertexPos, rot.Item1, rot.Item2 - rootJointPosition); // Thepivot is transformed to root joint coordinates because the vertices are in root joint coordinates
            // }

            // newVerticesPositions[vid] = (skeleton[influenceBoneId].transform.position - skeleton[0].transform.position) + verticesAndJointsOffsetsInTPose[vid][influenceBoneId];
            newVerticesPositions[vid] = skeleton[influenceBoneId].transform.TransformVector(verticesAndJointsOffsetsInTPose[vid][influenceBoneId]) + skeleton[influenceBoneId].transform.position - skeleton[0].transform.position;
        }

        return newVerticesPositions;
    }

    public Vector3[] LinearBlendSkinningFromOffsets(GameObject[] skeleton, Dictionary<int, Dictionary<int, Vector3>> verticesAndJointsOffsetsInTPose, Dictionary<int, VertexSkinningWeigts> vertexSkinningInfo) {
        
        Vector3[] newVerticesPositions = new Vector3[verticesAndJointsOffsetsInTPose.Count];

        // For each vertex
        for (int vid=0; vid<newVerticesPositions.Length; vid++) {

            for (int i=0; i<vertexSkinningInfo[vid].bonesIds.Length; i++) {

                int influenceBoneId = vertexSkinningInfo[vid].bonesIds[i];

                newVerticesPositions[vid] += vertexSkinningInfo[vid].weights[i] * (skeleton[influenceBoneId].transform.TransformVector(verticesAndJointsOffsetsInTPose[vid][influenceBoneId]) + skeleton[influenceBoneId].transform.position - skeleton[0].transform.position);
            }

        }

        return newVerticesPositions;
    }

    public Dictionary<int, Dictionary<int, Vector3>> GetVerticesAndJointsOffsetsInTPose(Dictionary<int, VertexSkinningWeigts> vertexSkinningInfo, Vector3[] vertices, GameObject[] skeletonTPose) {
        
        Dictionary<int, Dictionary<int, Vector3>> offsets = new Dictionary<int, Dictionary<int, Vector3>>();

        // For each vertex
        for (int vid=0; vid<vertices.Length; vid++) {

            offsets[vid] = new Dictionary<int, Vector3>();
            
            foreach (int influenceBoneId in vertexSkinningInfo[vid].bonesIds) {

                // offsets[vid][influenceBoneId] = vertices[vid] - (skeletonTPose[influenceBoneId].transform.position - skeletonTPose[0].transform.position);
                // Calculate the offset between the vertex and the joint, in hte joints local space.
                offsets[vid][influenceBoneId] = skeletonTPose[influenceBoneId].transform.InverseTransformVector(vertices[vid] - skeletonTPose[influenceBoneId].transform.position);
            }
        }

        return offsets;
    }
}

    


public static class Rotations {
    public static Vector3 Rotated(this Vector3 vector, Quaternion rotation, Vector3 pivot = default(Vector3)) {
        return rotation * (vector - pivot) + pivot;
    }

    public static Vector3 Rotated(this Vector3 vector, Vector3 rotation, Vector3 pivot = default(Vector3)) {
        return Rotated(vector, Quaternion.Euler(rotation), pivot);
    }

    public static Vector3 Rotated(this Vector3 vector, float x, float y, float z, Vector3 pivot = default(Vector3)) {
        return Rotated(vector, Quaternion.Euler(x, y, z), pivot);
    }

}