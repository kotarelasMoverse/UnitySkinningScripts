using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System;
using System.Data.Common;
using Unity.Mathematics;
//using Unity.Mathematics;


public class SimpleSkinningWithOffsets : MonoBehaviour
{
    [Range(-180, 180)]
    public int LHipXAngle;
    [Range(-180, 180)]
    public int LKneeXAngle;
    [Range(-180, 180)]
    public int LAnkleXAngle;
    [Range(-180, 180)]
    public int RShoulderZAngle;
    [Range(-180, 180)]
    public int RElbowYAngle;
    public bool useGPU = false;
    public ComputeShader SimpleSkinningComputeShader;
    private ComputeBuffer _verticesBuffer;
    ComputeBuffer animatedJointPositionsBuffer;
    ComputeBuffer verticesOffsetsBuffer;
    ComputeBuffer idsOfInfluenceJointsBuffer;
    ComputeBuffer jointsAccumRotationsBuffer;

    GameObject[] skeletonWithoutHierarchy;
    GameObject skeletonMeshWithoutHierarchy;
    GameObject skeletonWithHierarchy;
    GameObject skeletonMeshWithHierarchy;
    Dictionary<int, Transform> boneIdToSkeletonWithHierarchyBone;
    Dictionary<int, Dictionary<int, Vector3>> verticesAndJointsOffsets;
    Vector4[] verticesOffsetsInTPoseForShader;
    int[] idsOfInfluenceJointsForShader;
    int numberOfVertices;
    
    Transform[] inputBones;

    List<int[]> paths = new List<int[]>
        {
            new int[] { 0, 1, 4, 7, 10 },
            new int[] { 0, 2, 5, 8, 11 },
            new int[] { 0, 3, 6, 9, 12, 15 },
            new int[] { 0, 3, 6, 9, 13, 16, 18, 20, 22 },
            new int[] { 0, 3, 6, 9, 14, 17, 19, 21, 23 }
        };

    Dictionary<int, Vector3> animationParams;

    

    // Start is called before the first frame update
    void Start()
    {  
        GameObject inputModel = GameObject.Find("Clone");
        Transform skeletonTransform = inputModel.transform.GetChild(0); // The transform of the skeleton game object
        Transform root = skeletonTransform.GetChild(0); // The transform of the root joint (pelvis) game object
        SkinnedMeshRenderer rend = root.GetComponentInParent<SkinnedMeshRenderer>();

        // Set the model to T-pose
        Utils.SetModelToTPose(root);

        // Extract vertices position in T-pose, the position are in local coordinate system with center the center of the mesh (average of all the vertices positions and position of the mesh object)
        Mesh inputMesh = rend.sharedMesh;
        Vector3[] inputVerticesLocalCoord = inputMesh.vertices;
        numberOfVertices = inputVerticesLocalCoord.Length;
        Debug.Log("Clone mesh vertex number " + numberOfVertices);

        // Get center of mesh in world coordinates
        Vector3 inputMeshCenter = new Vector3();
        for (int i=0; i<inputVerticesLocalCoord.Length; i++) {
            inputMeshCenter += rend.localToWorldMatrix.MultiplyPoint(inputVerticesLocalCoord[i]);
        }
        inputMeshCenter = inputMeshCenter / inputVerticesLocalCoord.Length;
        Debug.Log("Clone mesh center in world coordinates" + inputMeshCenter);
        Debug.Log("Root position " + root.position);

        // Get the positions of the vertices in T-pose, in the world coordinate system
        Vector3[] inputVerticesWorldCoord = new Vector3[inputVerticesLocalCoord.Length];
        Matrix4x4 transfMatrix = rend.localToWorldMatrix;
        for (int i=0; i<inputVerticesLocalCoord.Length; i++) {
            inputVerticesWorldCoord[i] = transfMatrix.MultiplyPoint(inputVerticesLocalCoord[i]);
        }

        // Get the positions of the vertices in T-pose in the root joint coordinate system
        Vector3[] inputVerticesRootCoord = new Vector3[inputVerticesWorldCoord.Length];
        for (int i=0; i<inputVerticesWorldCoord.Length; i++) {
            inputVerticesRootCoord[i] = inputVerticesWorldCoord[i] - root.position;
        }

        // Get list of joints/bones in T-pose, with the index of the list corresponding to the id of the bone
        inputBones = rend.bones;

        // Get bone weights for the vertices 
        var inputBoneWeights = inputMesh.GetAllBoneWeights();
        var inputBonesPerVertex = inputMesh.GetBonesPerVertex();

        // For each vertex get the infuence bones and the corresponding weights.
        Dictionary<int, Utils.VertexSkinningWeigts> vertexSkinningInfo = Utils.GetVertexSkinningInfo(inputBoneWeights, inputBonesPerVertex);

        // Hypothetical animation parameters
        animationParams = new Dictionary<int, Vector3>(); 
        animationParams[1] = new Vector3(45, 0, 0); // Rotate L Hip around x by 45 degrees
        animationParams[4] = new Vector3(45, 0, 0); // Rotate L Knee around x by 60 degrees
        animationParams[7] = new Vector3(45, 0, 0); // Rotate L Ankle around x by 45 degrees
        animationParams[17] = new Vector3(0, 0, -30); // Rotate R shoulder around x by 90 degrees
        animationParams[19] = new Vector3(0, -100, 0); // Rotate R elbow around y by 90 degrees

        #region Skeleton without Hierarchy
        // Create a skeleton without hierarchy, just a list of game objects
        skeletonWithoutHierarchy = Utils.BuildSkeletonWithoutHierarchy(inputBones);

        // Get the offsets between the vertices and the influence bones, in T-pose
        // verticesAndJointsOffsets = Utils.GetVerticesAndJointsOffsetsInTPose(vertexSkinningInfo, inputVerticesWorldCoord, skeletonWithoutHierarchy);
        verticesAndJointsOffsets = Utils.GetVerticesAndJointsOffsetsInTPoseInWorldSpcae(vertexSkinningInfo, inputVerticesWorldCoord, skeletonWithoutHierarchy);

        // Create a new mesh at the position of the skeleton without hierarchy
        skeletonMeshWithoutHierarchy = Utils.CreateNewMeshAtPosition(skeletonWithoutHierarchy[0].transform, inputVerticesWorldCoord); // Pass the vertices position wrt the root bone (pelvis), so the vertices of the new mesh are also in the coordinate system of the root bone
        skeletonMeshWithoutHierarchy.name = "SimpleSkinningMeshNoHierarchy";

        // Apply the animation parameters to the skeleton without hierarchy
        Utils.AnimateSkeletonWithoutHierarchy(skeletonWithoutHierarchy, animationParams, inputBones, paths);

        // Apply simple skinning to the skeleton without hierarchy
        // Vector3[] simpleSkinnedSkeletonMeshVertices = SimpleSkinningFromOffsetsWithoutHierarchy(skeletonWithoutHierarchy, verticesAndJointsOffsets);
        Vector3[] simpleSkinnedSkeletonMeshVertices = SimpleSkinningFromOffsetsWithoutHierarchyInWorldSpace(skeletonWithoutHierarchy, verticesAndJointsOffsets, animationParams);
        skeletonMeshWithoutHierarchy.GetComponent<MeshFilter>().mesh.vertices = simpleSkinnedSkeletonMeshVertices;
        #endregion

        #region Skeleton with hierarchy
        // Create a skeleton with hierarchy
        skeletonWithHierarchy = Utils.BuildSkeletonWithHierarchy(root, null);

        // Associate the bones of the skeleton with hierarchy, with the boneIds
        boneIdToSkeletonWithHierarchyBone = new Dictionary<int, Transform>();
        Transform[] skeletonWithHierarchyArray = skeletonWithHierarchy.GetComponentsInChildren<Transform>();
        for (int b=0; b<inputBones.Length; b++) {
            int boneId = Array.FindIndex(skeletonWithHierarchyArray, x => x.name == inputBones[b].name);
            boneIdToSkeletonWithHierarchyBone[b] = skeletonWithHierarchyArray[boneId];
        } 

        // Create a new mesh at the position of skeleton with hierarchy
        skeletonMeshWithHierarchy = Utils.CreateNewMeshAtPosition(skeletonWithHierarchy.transform, inputVerticesRootCoord);
        skeletonMeshWithHierarchy.name = "SimpleSkinningMeshHierarchy";

        // Apply the animation parameters to the skeleton with hierarchy
        Utils.AnimateSkeletonWithHierarchy(skeletonWithHierarchy.transform, animationParams, inputBones);

        // Get the accumulated rotations that each joint will impose to the vertices that it influences
        Matrix4x4[] jointsAccumulatedRotations = Utils.GetJointsAccumulatedRotations(animationParams, paths, inputBones.Length);

        // Vector3[] simpleSkinnedSkeletonMeshVerticesWithHierarchy = SimpleSkinningFromOffsetsWithHierarchy(boneIdToSkeletonWithHierarchyBone, verticesAndJointsOffsets);
        Vector3[] simpleSkinnedSkeletonMeshVerticesWithHierarchy = SimpleSkinningFromOffsetsWithHierarchyInWorldSpace(boneIdToSkeletonWithHierarchyBone, verticesAndJointsOffsets, jointsAccumulatedRotations);
        skeletonMeshWithHierarchy.GetComponent<MeshFilter>().mesh.vertices = simpleSkinnedSkeletonMeshVerticesWithHierarchy;
        #endregion


        #region GPU initializations
        // Get the offset of each vertex with its most influential joint in an array of Vector4s
        verticesOffsetsInTPoseForShader = new Vector4[numberOfVertices];
        idsOfInfluenceJointsForShader = new int[numberOfVertices];
        for (int vid=0; vid<numberOfVertices; vid++) {
            int influenceBoneId = verticesAndJointsOffsets[vid].Keys.ToList()[0];
            verticesOffsetsInTPoseForShader[vid] = verticesAndJointsOffsets[vid][influenceBoneId];
            idsOfInfluenceJointsForShader[vid] = influenceBoneId;
        }

        // Initialize the vertices buffer
        _verticesBuffer = new ComputeBuffer(numberOfVertices*3, sizeof(float));
        animatedJointPositionsBuffer = new ComputeBuffer(inputBones.Length*4, sizeof(float));
        verticesOffsetsBuffer = new ComputeBuffer(numberOfVertices*4, sizeof(float));
        verticesOffsetsBuffer.SetData(verticesOffsetsInTPoseForShader);
        idsOfInfluenceJointsBuffer = new ComputeBuffer(numberOfVertices, sizeof(int));
        idsOfInfluenceJointsBuffer.SetData(idsOfInfluenceJointsForShader);
        jointsAccumRotationsBuffer = new ComputeBuffer(inputBones.Length*16, sizeof(float));
        #endregion
    }

    // Update is called once per frame
    void Update()
    {   
        // Simulate input animation parameters
        animationParams[1] = new Vector3(LHipXAngle, 0, 0); // Rotate L Hip around x by 45 degrees
        animationParams[4] = new Vector3(LKneeXAngle, 0, 0); // Rotate L Knee around x by 60 degrees
        animationParams[7] = new Vector3(LAnkleXAngle, 0, 0); // Rotate L Ankle around x by 45 degrees
        animationParams[17] = new Vector3(0, 0, RShoulderZAngle); // Rotate R shoulder around x by 90 degrees
        animationParams[19] = new Vector3(0, RElbowYAngle, 0); // Rotate R elbow around y by 90 degrees


        // Real time skinning for the skeleton with hierarchy
        // Vector3[] simpleSkinnedSkeletonMeshVertices = SimpleSkinningFromOffsets(skeletonWithoutHierarchy, verticesAndJointsOffsets);
        // skeletonMeshWithoutHierarchy.GetComponent<MeshFilter>().mesh.vertices = simpleSkinnedSkeletonMeshVertices;

        if (!useGPU) {
            // Real time skinning for the skeleton with hierarchy in CPU
            Utils.SetModelToTPose(skeletonWithHierarchy.transform);
            Utils.AnimateSkeletonWithHierarchy(skeletonWithHierarchy.transform, animationParams, inputBones);
            // Get the accumulated rotations that each joint will impose to the vertices that it influences
            Matrix4x4[] jointsAccumulatedRotations = Utils.GetJointsAccumulatedRotations(animationParams, paths, inputBones.Length);

            // Vector3[] simpleSkinnedSkeletonMeshVerticesWithHierarchy = SimpleSkinningFromOffsetsWithHierarchy(boneIdToSkeletonWithHierarchyBone, verticesAndJointsOffsets);
            Vector3[] simpleSkinnedSkeletonMeshVerticesWithHierarchy = SimpleSkinningFromOffsetsWithHierarchyInWorldSpace(boneIdToSkeletonWithHierarchyBone, verticesAndJointsOffsets, jointsAccumulatedRotations);
            skeletonMeshWithHierarchy.GetComponent<MeshFilter>().mesh.vertices = simpleSkinnedSkeletonMeshVerticesWithHierarchy;
        }
        else {
            Utils.SetModelToTPose(skeletonWithHierarchy.transform);
            Utils.AnimateSkeletonWithHierarchy(skeletonWithHierarchy.transform, animationParams, inputBones);
            // Get the accumulated rotations that each joint will impose to the vertices that it influences
            Matrix4x4[] jointsAccumulatedRotations = Utils.GetJointsAccumulatedRotations(animationParams, paths, inputBones.Length);

            // Get the animated joint positions in an array of Vector4s
            Vector4[] animatedJointPositionsForShader = new Vector4[inputBones.Length];
            Transform[] skeletonWithHierarchyArray = skeletonWithHierarchy.GetComponentsInChildren<Transform>();
            for (int b=0; b<inputBones.Length; b++) {
                int boneId = Array.FindIndex(skeletonWithHierarchyArray, x => x.name == inputBones[b].name);
                animatedJointPositionsForShader[b] = skeletonWithHierarchyArray[boneId].position;
            }

            // // Get the offset of each vertex with its most influential joint in an array of Vector4s
            // Vector4[] verticesOffsetsInTPoseForShader = new Vector4[numberOfVertices];
            // int[] idsOfInfluenceJointsForShader = new int[numberOfVertices];
            // for (int vid=0; vid<numberOfVertices; vid++) {
            //     int influenceBoneId = verticesAndJointsOffsets[vid].Keys.ToList()[0];
            //     verticesOffsetsInTPoseForShader[vid] = verticesAndJointsOffsets[vid][influenceBoneId];
            //     idsOfInfluenceJointsForShader[vid] = influenceBoneId;
            // }

            

            // ComputeBuffer animatedJointPositionsBuffer = new ComputeBuffer(inputBones.Length*4, sizeof(float));
            animatedJointPositionsBuffer.SetData(animatedJointPositionsForShader);

            // ComputeBuffer verticesOffsetsBuffer = new ComputeBuffer(numberOfVertices*4, sizeof(float));
            // verticesOffsetsBuffer.SetData(verticesOffsetsInTPoseForShader);

            // ComputeBuffer idsOfInfluenceJointsBuffer = new ComputeBuffer(numberOfVertices, sizeof(int));
            // idsOfInfluenceJointsBuffer.SetData(idsOfInfluenceJointsForShader);

            // ComputeBuffer jointsAccumRotationsBuffer = new ComputeBuffer(inputBones.Length*16, sizeof(float));
            jointsAccumRotationsBuffer.SetData(jointsAccumulatedRotations);

            float[] flattenedVertices = new float[numberOfVertices*3];

            // ComputeBuffer animatedJointPositionsBufferRet = new ComputeBuffer(inputBones.Length*4, sizeof(float));
            // float[] flattenedjointPositions = new float[inputBones.Length*4];
            // ComputeBuffer verticesOffsetsBufferRet = new ComputeBuffer(numberOfVertices*4, sizeof(float));
            // float[] flattenedOffsets = new float[numberOfVertices*4];
            // ComputeBuffer idsOfInfluenceJointsBufferRet = new ComputeBuffer(numberOfVertices, sizeof(int));
            // int[] flattenedids = new int[numberOfVertices];
            // ComputeBuffer jointsAccumRotationsBufferRet = new ComputeBuffer(inputBones.Length*16, sizeof(float));
            // float[] flattenedRotations = new float[inputBones.Length*16];

            int kernel = SimpleSkinningComputeShader.FindKernel("SimpleSkinning");
            SimpleSkinningComputeShader.SetBuffer(kernel, "_VertexBuffer", _verticesBuffer);
            SimpleSkinningComputeShader.SetInt("_NumVertices", Mathf.CeilToInt(numberOfVertices));
            SimpleSkinningComputeShader.SetBuffer(kernel, "_AnimatedJointPositions", animatedJointPositionsBuffer);
            SimpleSkinningComputeShader.SetBuffer(kernel, "_VerticesOffsetsWithJoints", verticesOffsetsBuffer);
            SimpleSkinningComputeShader.SetBuffer(kernel, "_IdsOfInfluenceJointsPerVertex", idsOfInfluenceJointsBuffer);
            SimpleSkinningComputeShader.SetBuffer(kernel, "_JointsAccumulatedRotations", jointsAccumRotationsBuffer);

            // SimpleSkinningComputeShader.SetBuffer(kernel, "_AnimatedJointPositionsRet", animatedJointPositionsBufferRet);
            // SimpleSkinningComputeShader.SetBuffer(kernel, "_VerticesOffsetsWithJointsRet", verticesOffsetsBufferRet);
            // SimpleSkinningComputeShader.SetBuffer(kernel, "_IdsOfInfluenceJointsPerVertexRet", idsOfInfluenceJointsBufferRet);
            // SimpleSkinningComputeShader.SetBuffer(kernel, "_JointsAccumulatedRotationsRet", jointsAccumRotationsBufferRet);

            SimpleSkinningComputeShader.Dispatch(kernel, Mathf.CeilToInt(numberOfVertices / 1024.0f), 1, 1);
            _verticesBuffer.GetData(flattenedVertices);
            // print(flattenedVertices.Max());
            // print(flattenedVertices.Min());

            // animatedJointPositionsBufferRet.GetData(flattenedjointPositions);
            // verticesOffsetsBufferRet.GetData(flattenedOffsets);
            // idsOfInfluenceJointsBufferRet.GetData(flattenedids);
            // jointsAccumRotationsBufferRet.GetData(flattenedRotations);

            // From flattened to vector3 array
            Vector3[] newVertices = new Vector3[numberOfVertices];
            for (int vid=0; vid<numberOfVertices; vid++) {
                newVertices[vid] = new Vector3(flattenedVertices[vid*3], flattenedVertices[vid*3+1], flattenedVertices[vid*3+2]);
            }
            
            skeletonMeshWithHierarchy.GetComponent<MeshFilter>().mesh.vertices = newVertices;
        }
        


    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="skeleton">GameObjects of the skeleton joints on the animated position.</param>
    /// <param name="verticesAndJointsOffsetsInTPose">Dictionary that associates each vertex id with the id's of the bones that infuence this vertex and the corresponding skin weights.</param>
    /// <returns></returns>
    public Vector3[] SimpleSkinningFromOffsetsWithoutHierarchy(GameObject[] skeleton, Dictionary<int, Dictionary<int, Vector3>> verticesAndJointsOffsetsInTPose) {
        
        Vector3[] newVerticesPositions = new Vector3[verticesAndJointsOffsetsInTPose.Count];

        // For each vertex
        for (int vid=0; vid<newVerticesPositions.Length; vid++) {

            // Get the id of the bone that influences this vertex, with the largest weight (first bone)
            int influenceBoneId = verticesAndJointsOffsetsInTPose[vid].Keys.ToList()[0];

            // Calculate the new position of the vertex by adding the offset to the position of the bone that it is infuenced by
            // The offsets are calculated wrt the root joint, so the joint position should also be converted to coordinates wrt the root joint.
            newVerticesPositions[vid] = skeleton[influenceBoneId].transform.TransformVector(verticesAndJointsOffsetsInTPose[vid][influenceBoneId]) + skeleton[influenceBoneId].transform.position - skeleton[0].transform.position;
        }

        return newVerticesPositions;
    }
    public Vector3[] SimpleSkinningFromOffsetsWithoutHierarchyInWorldSpace(GameObject[] skeleton, Dictionary<int, Dictionary<int, Vector3>> verticesAndJointsOffsetsInTPose, Dictionary<int, Vector3> animation) {
        
        Vector3[] newVerticesPositions = new Vector3[verticesAndJointsOffsetsInTPose.Count];

        // Get the accumulated rotations that each joint will impose to the vertices that it influences
        Matrix4x4[] jointIdToAccumulatedRotations = new Matrix4x4[skeleton.Length];
        for (int bId=0; bId<jointIdToAccumulatedRotations.Length; bId++) {

            jointIdToAccumulatedRotations[bId] = Matrix4x4.Rotate(Quaternion.identity);

            int[] parentJointsOfCurrentJoint = Utils.FindParentJointsOf(bId, paths);
            foreach (int jid in parentJointsOfCurrentJoint) {
                if (animation.Keys.ToList().Contains(jid)) {
                    jointIdToAccumulatedRotations[bId] *= Matrix4x4.Rotate(Quaternion.Euler(animation[jid]));
                }
            }
        }

        // For each vertex
        for (int vid=0; vid<newVerticesPositions.Length; vid++) {

            // Get the id of the bone that influences this vertex, with the largest weight (first bone)
            int influenceBoneId = verticesAndJointsOffsetsInTPose[vid].Keys.ToList()[0];
            // int[] parentJointsOfCurrentJoint = Utils.FindParentJointsOf(influenceBoneId, paths);
            // Matrix4x4 rot = Matrix4x4.Rotate(Quaternion.identity);

            // foreach (int jid in parentJointsOfCurrentJoint) {
            //     if (animation.Keys.ToList().Contains(jid)) {
            //         rot *= Matrix4x4.Rotate(Quaternion.Euler(animation[jid]));
            //     }
            // }
            
            // Calculate the new position of the vertex by adding the offset to the position of the bone that it is infuenced by
            // The offsets are calculated wrt the root joint, so the joint position should also be converted to coordinates wrt the root joint.
            newVerticesPositions[vid] = jointIdToAccumulatedRotations[influenceBoneId].MultiplyPoint(verticesAndJointsOffsetsInTPose[vid][influenceBoneId]) + skeleton[influenceBoneId].transform.position;
        }

        return newVerticesPositions;
    }


    public Vector3[] SimpleSkinningFromOffsetsWithHierarchy(Dictionary<int, Transform> boneIdToSkeletonWithHierarchyBone, Dictionary<int, Dictionary<int, Vector3>> verticesAndJointsOffsetsInTPose) {
        
        Vector3[] newVerticesPositions = new Vector3[verticesAndJointsOffsetsInTPose.Count];

        // For each vertex
        for (int vid=0; vid<newVerticesPositions.Length; vid++) {

            // Get the id of the bone that influences this vertex, with the largest weight (first bone)
            int influenceBoneId = verticesAndJointsOffsetsInTPose[vid].Keys.ToList()[0];

            // Calculate the new position of the vertex by adding the offset to the position of the bone that it is infuenced by
            // The offsets are calculated wrt the root joint, so the joint position should also be converted to coordinates wrt the root joint.
            newVerticesPositions[vid] = boneIdToSkeletonWithHierarchyBone[influenceBoneId].TransformVector(verticesAndJointsOffsetsInTPose[vid][influenceBoneId]) + boneIdToSkeletonWithHierarchyBone[influenceBoneId].position - boneIdToSkeletonWithHierarchyBone[0].position;
        }

        return newVerticesPositions;
    }

    public Vector3[] SimpleSkinningFromOffsetsWithHierarchyInWorldSpace(Dictionary<int, Transform> boneIdToSkeletonWithHierarchyBone, Dictionary<int, Dictionary<int, Vector3>> verticesAndJointsOffsetsInTPose, Matrix4x4[] jointIdToAccumulatedRotations) {
        
        Vector3[] newVerticesPositions = new Vector3[verticesAndJointsOffsetsInTPose.Count];

        // Get the accumulated rotations that each joint will impose to the vertices that it influences
        // Matrix4x4[] jointIdToAccumulatedRotations = new Matrix4x4[boneIdToSkeletonWithHierarchyBone.Keys.ToList().Count];
        // for (int bId=0; bId<jointIdToAccumulatedRotations.Length; bId++) {

        //     jointIdToAccumulatedRotations[bId] = Matrix4x4.Rotate(Quaternion.identity);

        //     int[] parentJointsOfCurrentJoint = Utils.FindParentJointsOf(bId, paths);
        //     foreach (int jid in parentJointsOfCurrentJoint) {
        //         if (animation.Keys.ToList().Contains(jid)) {
        //             jointIdToAccumulatedRotations[bId] *= Matrix4x4.Rotate(Quaternion.Euler(animation[jid]));
        //         }
        //     }
        // }

        // For each vertex
        for (int vid=0; vid<newVerticesPositions.Length; vid++) {

            // Get the id of the bone that influences this vertex, with the largest weight (first bone)
            int influenceBoneId = verticesAndJointsOffsetsInTPose[vid].Keys.ToList()[0];
            // int[] parentJointsOfCurrentJoint = Utils.FindParentJointsOf(influenceBoneId, paths);
            // Matrix4x4 rot = Matrix4x4.Rotate(Quaternion.identity);

            // foreach (int jid in parentJointsOfCurrentJoint) {
            //     if (animation.Keys.ToList().Contains(jid)) {
            //         rot *= Matrix4x4.Rotate(Quaternion.Euler(animation[jid]));
            //     }
            // }

            // Calculate the new position of the vertex by adding the offset to the position of the bone that it is infuenced by
            // The offsets are calculated wrt the root joint, so the joint position should also be converted to coordinates wrt the root joint.
            newVerticesPositions[vid] = jointIdToAccumulatedRotations[influenceBoneId].MultiplyPoint(verticesAndJointsOffsetsInTPose[vid][influenceBoneId]) + boneIdToSkeletonWithHierarchyBone[influenceBoneId].position;
        }

        return newVerticesPositions;
    }

    // void OnApplicationQuit()
    // {
    //     _verticesBuffer.Release();
    //     animatedJointPositionsBuffer.Release();
    //     verticesOffsetsBuffer.Release();
    //     idsOfInfluenceJointsBuffer.Release();
    //     jointsAccumRotationsBuffer.Release();
    // }
}
