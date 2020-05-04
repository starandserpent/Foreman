using System.Diagnostics;
using GodotVector3 = Godot.Vector3;
using System.Collections.Generic;
using Threading = System.Threading.Thread;

public class Foreman {
	//This is recommend max static octree size because it takes 134 MB
	private volatile GodotMesher mesher;
	private volatile Octree octree;
	private int grassMeshID;
	private volatile Weltschmerz weltschmerz;
	private volatile Terra terra;
	private int maxViewDistance;
	private float fov;
	private int generationThreads;
	private ChunkFiller chunkFiller;
	private List<GodotVector3> localCenters;
	private volatile bool runThread = true;
	private volatile List<long> chunkSpeed;
	private Threading[] threads;
	private volatile Godot.Spatial loadMarker = null;
	private int location;
	public volatile static int chunksLoaded = 0;
	public volatile static int chunksPlaced = 0;
	public volatile static int positionsScreened = 0;
	public volatile static int positionsInRange = 0;

	public volatile static int positionsNeeded = 0;

	private Godot.Transform lastTransform;

	private Stopwatch stopwatch;

	public Foreman (Weltschmerz weltschmerz, Terra terra, Registry registry, GodotMesher mesher,
		int viewDistance, float fov, int generationThreads) {
		this.weltschmerz = weltschmerz;
		this.terra = terra;
		lastTransform = Godot.Transform.Identity;
		this.octree = terra.GetOctree ();
		maxViewDistance = viewDistance;
		this.fov = fov;
		this.mesher = mesher;
		this.generationThreads = generationThreads;
		localCenters = new List<GodotVector3> ();
		chunkSpeed = new List<long> ();
		threads = new Threading[generationThreads];
		stopwatch = new Stopwatch ();

		for (int t = 0; t < generationThreads; t++) {
			threads[t] = new Threading (() => Process ());
			threads[t].Start ();
		}

		for (int l = -maxViewDistance; l < maxViewDistance; l += 8) {
			for (int y = -Utils.GetPosFromFOV (fov, l); y < Utils.GetPosFromFOV (fov, l); y += 8) {
				for (int x = -Utils.GetPosFromFOV (fov, l); x < Utils.GetPosFromFOV (fov, l); x += 8) {
					GodotVector3 center = new GodotVector3 (x, y, -l);
					localCenters.Add (center);
				}
			}
		}
	}

	public void AddLoadMarker (Godot.Spatial loadMarker) {
		if (this.loadMarker == null) {
			this.loadMarker = loadMarker;
			this.lastTransform = loadMarker.GlobalTransform;
		}
	}

	//Initial generation

	public void Process () {
		Position pos = new Position ();
		while (runThread) {
			if (loadMarker != null) {
				GodotVector3 position = new GodotVector3 ();
				lock (this) {
					if (!lastTransform.Equals (loadMarker.GlobalTransform)) {
						location = 0;
						lastTransform = loadMarker.GlobalTransform;
					} else if (location < localCenters.Count) {
						position = loadMarker.ToGlobal (localCenters[location]) / 8;
						pos.x = (int) position.x;
						pos.y = (int) position.y;
						pos.z = (int) position.z;
						location++;

						OctreeNode node = terra.TraverseOctree (pos.x, pos.y, pos.z, 0);
						if (node != null && node.chunk != null) {
							pos.x = -1;
						}
					}
				}

				if (pos.x >= 0 && pos.z >= 0 && pos.y >= 0 && pos.x * 8 <= octree.sizeX &&
					pos.y * 8 <= octree.sizeY && pos.z * 8 <= octree.sizeZ) {
					LoadArea (pos.x, pos.y, pos.z);
				}
			}
		}
	}

	//Loads chunks
	private void LoadArea (int x, int y, int z) {
		//OctreeNode childNode = new OctreeNode();
		if (chunksPlaced < 1) {
			stopwatch.Start ();
		}

		Chunk chunk;
		if (y << Constants.CHUNK_EXPONENT > weltschmerz.GetConfig ().elevation.max_elevation) {
			chunk = new Chunk ();
			chunk.isEmpty = true;
			chunk.x = (uint) x << Constants.CHUNK_EXPONENT;
			chunk.y = (uint) y << Constants.CHUNK_EXPONENT;
			chunk.z = (uint) z << Constants.CHUNK_EXPONENT;
		} else {
			chunk = chunkFiller.GenerateChunk (x << Constants.CHUNK_EXPONENT, y << Constants.CHUNK_EXPONENT,
				z << Constants.CHUNK_EXPONENT, weltschmerz);
			if (!chunk.isSurface) {
				/*
				childNode.materialID = (int) chunk.voxels[0];
				chunk.voxels = new uint[1];
				chunk.voxels[0] = (uint) childNode.materialID;
				*/
				var temp = chunk.voxels[0];
				chunk.voxels = new Run[1];
				chunk.voxels[0] = temp;
				chunk.x = (uint) x << Constants.CHUNK_EXPONENT;
				chunk.y = (uint) y << Constants.CHUNK_EXPONENT;
				chunk.z = (uint) z << Constants.CHUNK_EXPONENT;
			}
		}
		chunksPlaced++;
		terra.PlaceChunk (x, y, z, chunk);
		if (!chunk.isEmpty) {
			mesher.MeshChunk (chunk);
		}

		if (chunksPlaced >= 500) {
			stopwatch.Stop ();
			Godot.GD.Print ("500 chunks took " + stopwatch.ElapsedMilliseconds + " ms");
			chunksPlaced = 0;
			stopwatch.Restart ();
		}
	}

	public void SetMaterials (Registry registry) {
		chunkFiller = new ChunkFiller(registry.SelectByName ("dirt").worldID, registry.SelectByName ("grass").worldID);
	}

	public void Stop () {
		runThread = false;
		for (int t = 0; t < generationThreads; t++) {
			threads[t].Abort ();
		}
		localCenters.Clear ();
	}

	public List<long> GetMeasures () {
		return chunkSpeed;
	}
}