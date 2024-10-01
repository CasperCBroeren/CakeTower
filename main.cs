using Godot;
using System;
using static Godot.RigidBody3D;

public partial class main : Node
{
	private int layers = 0;
	private Camera3D camera;
	private Tween currentTween;
	private Random random;
	private Node meshSlicer;
	private RigidBody3D cakeNext;
	private Material sliceMaterial;

	private double gravity;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		random = new Random();
		camera = GetNode<Camera3D>("TheCamera");
		CreateNewLayer();
		sliceMaterial = ResourceLoader.Load<Material>("res://sliceMaterial.material");
		var script = GD.Load<GDScript>("res://ConcaveMeshSlicer.gd");
		meshSlicer = (Node)script.New();
		AddChild(meshSlicer);
		gravity = ProjectSettings.GetSetting("physics/3d/default_gravity").AsDouble();

	}

	private void CreateNewLayer()
	{
		var cake = GetNode<RigidBody3D>("cake0");
		layers++;

		cakeNext = cake.Duplicate() as RigidBody3D;
		cakeNext.Name = "cake" + layers;

		AddChild(cakeNext);

		currentTween = cakeNext.CreateTween();
		currentTween.Finished += CurrentTween_Finished;
		var rightSide = false; // for now no right side slide// random.Next(1, 10) % 3 == 0;
		var newHeight = 0.67f + (layers * 0.20f);
		if (rightSide)
		{
			cakeNext.Position = new Vector3(-3.111f, newHeight, 0);
		}
		else
		{
			cakeNext.Position = new Vector3(0, newHeight, 3.111f);
		}

		var targetLocation = new Vector3(-3.111f, cakeNext.Position.Y, 3.111f);

		currentTween.TweenProperty(cakeNext, "position", targetLocation, 2 + 2 * random.NextDouble());
		if (layers % 4 == 0)
		{
			GD.Print("Move camera");
			camera.Position += new Vector3(0, 0.75f, 0);
			camera.LookAt(targetLocation);
		}
	}

	private void CurrentTween_Finished()
	{
		CreateNewLayer();
	}

	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("Place"))
		{ 
			currentTween.Kill();
			var slicePlane = GetNode<Node3D>("slicePlane");
			var cakeMesh = cakeNext.GetChild<MeshInstance3D>(0);
			var collision = cakeNext.GetChild<CollisionShape3D>(1); 
			
			var transform = Transform3D.Identity;
			transform.Origin = cakeMesh.ToLocal(slicePlane.GlobalTransform.Origin);
			transform.Basis.X = cakeMesh.ToLocal(slicePlane.GlobalTransform.Basis.X+cakeNext.GlobalPosition);
			transform.Basis.Y = cakeMesh.ToLocal(slicePlane.GlobalTransform.Basis.Y+cakeNext.GlobalPosition);
			transform.Basis.Z = cakeMesh.ToLocal(slicePlane.GlobalTransform.Basis.Z+cakeNext.GlobalPosition);
	 
			var slicedMeshes = meshSlicer.Call("slice_mesh", slicePlane.Transform, cakeMesh.Mesh, sliceMaterial);
			var meshes = slicedMeshes.AsGodotArray<Mesh>();
			cakeMesh.Mesh = meshes[0];

			if (meshes.Count > 1)
			{
				if (meshes[0].GetFaces().Length > 2)
				{
					collision.Shape = meshes[0].CreateConvexShape();
				}
				cakeNext.CenterOfMassMode = CenterOfMassModeEnum.Custom;
				cakeNext.CenterOfMass = cakeNext.ToLocal(cakeMesh.ToGlobal(CalculateCenterOfMass(meshes[0] as ArrayMesh)));

				var volume1 = CalculateMeshVolume(meshes[0] as ArrayMesh);
				var volume2 = CalculateMeshVolume(meshes[1] as ArrayMesh);
				var totalVolume = volume1 + volume2;

				var mass1 = cakeNext.Mass * (volume1 / totalVolume);
				var mass2 = cakeNext.Mass * (volume2 / totalVolume);

				cakeNext.Mass = mass1;
				// Create new part
				var secondHalf = cakeNext.Duplicate() as RigidBody3D;
				AddChild(secondHalf);
				if (secondHalf != null)
				{
					cakeMesh = secondHalf.GetChild<MeshInstance3D>(0);
					collision = secondHalf.GetChild<CollisionShape3D>(1);                    
					cakeMesh.Mesh = meshes[1];
					secondHalf.Mass = mass2;

					if (meshes[1].GetFaces().Length > 2)
					{
						collision.Shape = meshes[1].CreateConvexShape();
					}

					//get mesh size
					var aabb = meshes[0].GetAabb();
					var aabb2 = meshes[1].GetAabb();

					// queue_free() if the mesh is too small
					if (aabb2.Size.Length() < 0.3f)
					{
						secondHalf.QueueFree();
					}
					if (aabb.Size.Length() < 0.3f)
					{
						cakeNext.QueueFree();
					}
					// adjust the rigidbody center of mass
					secondHalf.CenterOfMass = secondHalf.ToLocal(cakeMesh.ToGlobal(CalculateCenterOfMass(meshes[1] as ArrayMesh)));
				}
				else
				{
					GD.Print("No second half");
				}
			}
			else
			{
				GD.Print("No second slice 2");
			}
			//https://github.com/PiCode9560/Godot-4-Concave-Mesh-Slicer/blob/main/Godot%204%20Concave%20Mesh%20Slicer/Example/Player.gd

			CreateNewLayer();
		}
	}

	private Vector3 CalculateCenterOfMass(ArrayMesh mesh)
	{
		var meshVolume = 0.0f;
		var temp = new Vector3(0, 0, 0);
		for (var i = 0; i < mesh.GetFaces().Length / 3; i++)
		{
			var v1 = mesh.GetFaces()[i];
			var v2 = mesh.GetFaces()[i + 1];
			var v3 = mesh.GetFaces()[i + 2];

			var center = (v1 + v2 + v3) / 3;
			var volume = (Geometry3D
				.GetClosestPointToSegmentUncapped(v3, v1, v2)
				.DistanceTo(v3)
				*
				v1.DistanceTo(v2)) / 2;
			meshVolume += volume;
			temp += center * volume;
		}

		return meshVolume == 0 ? Vector3.Zero : temp / meshVolume;
	}

	private float CalculateMeshVolume(ArrayMesh mesh)
	{
		var volume = 0.0f;
		for (var surface = 0; surface < mesh.GetSurfaceCount(); surface++)
		{
			var arrays = mesh.SurfaceGetArrays(surface);
			var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsGodotArray<Vector3>();
			for (var i = 0; i < vertices.Count; i += 3)
			{
				var v1 = vertices[i];
				var v2 = vertices[i + 1];
				var v3 = vertices[i + 2];

				volume += Math.Abs(v1.Dot(v2.Cross(v3))) / 6.0f;
			}
		}
		return volume;
	}
}
