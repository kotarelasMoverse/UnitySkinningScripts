# High Priority:
- ~~In the Skinning methods change the 'TransformVector' functions. This will have to be handled by the shader so it should be calculated manually.~~



# Low Priority:
- Build skeleton not from the root joint but from the positions/rotations of the joints (coupled with the joint ids/names). In the proper use case I will probably not have the hierarchy available.
- Replace Matrix4x4 with 3x3 matrices. (https://github.com/Azure/Accord-NET/blob/master/Sources/Accord.Math/AForge.Math/Matrix3x3.cs)
- Change the inputs so that they have proper structures.
- Normalize skinning weights.