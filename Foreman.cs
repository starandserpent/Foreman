using System.Collections.Concurrent;
using System.Buffers;
public class Foreman {
	private volatile Weltschmerz weltschmerz;
	private int radius;
	private Terra terra;
	private Registry registry;
	public int chunks;

	private ConcurrentQueue<Position> fillPositions;

	public Foreman (Weltschmerz weltschmerz, Registry registry, Terra terra, int radius)
	{
		fillPositions = new ConcurrentQueue<Position>();
		this.weltschmerz = weltschmerz;
		this.radius = radius;
		this.terra = terra;
		this.registry = registry;
	}

	public void Fill()
	{
		Position position;

		while(!fillPositions.IsEmpty)
		{
		
		fillPositions.TryDequeue(out position);

		int x = position.x;
		int y = position.y;
		int z = position.z;

		if (terra.CheckBoundries (x, y, z)) {
			OctreeNode node = terra.TraverseOctree(x, y, z, 0);
			if(node != null){
				Chunk chunk;
				if(node.chunk == null){
					chunk = new Chunk();
							node.chunk = chunk;

								chunk.x = (uint) (x << Constants.CHUNK_EXPONENT);
								chunk.y = (uint) (y << Constants.CHUNK_EXPONENT);
								chunk.z = (uint) (z << Constants.CHUNK_EXPONENT);
							}else
							{
								chunk = node.chunk;
							}

							AddMaterialsToChunk(registry, chunk);
						}
			}
		}
	}

	public void SetOrigin(int originX, int originY, int originZ)
	{
		for(int x = originX - radius; x < originX + radius; x++)
		{
			for(int y = originX - radius; y < originY + radius; y++)
			{
				for(int z = originX - radius; z < originZ + radius; z++)
				{
					fillPositions.Enqueue(new Position(x, y, z));
				}
			}
		}
	}

	private void AddMaterialsToChunk(Registry registry, Chunk chunk)
	{
		int[] tempChunk = ArrayPool<int>.Shared.Rent(Constants.CHUNK_SIZE3D);

		foreach(TerraObject tobject in registry)
		{
			chunk.Materials ++;

			if(tobject.worldID != 0)
			{
				if(tobject.IsSurface)
				{
					FillChunkSurface (chunk, tempChunk, tobject.worldID);
				}
				else
				{
					FillChunk (chunk, tempChunk, tobject.worldID);
				}
			}
		}

		ParseToRLE(chunk, tempChunk);

		if(!chunk.IsSolid){
			MakeChunkBorders(chunk, tempChunk);
		}

		chunks ++;
		ArrayPool<int>.Shared.Return(tempChunk);
	}

	//Loads chunks
	private void FillChunkSurface (Chunk chunk, int[] tempChunk, int materialID) 
	{   
		for(int x = 0; x < Constants.CHUNK_SIZE1D; x++)
		{
			for(int z = 0; z < Constants.CHUNK_SIZE1D; z++)
			{
				double elevation = weltschmerz.GetElevation(x + (int) (chunk.x / Constants.VOXEL_SIZE), z + (int) (chunk.z / Constants.VOXEL_SIZE));
				int chunkElevation = (int) elevation/Constants.CHUNK_SIZE1D;
				if(chunkElevation == (int) (chunk.y / Constants.CHUNK_LENGHT))
				{
					tempChunk[(int)(elevation%Constants.CHUNK_SIZE1D) + x * Constants.CHUNK_SIZE1D + z * Constants.CHUNK_SIZE2D] = materialID;
				}
			}
		}
	}

	private void FillChunk (Chunk chunk, int[] tempChunk, int materialID) {

		for(int x = 0; x < Constants.CHUNK_SIZE1D; x++)
		{
			for(int z = 0; z < Constants.CHUNK_SIZE1D; z++)
			{
				int elevation = (int) weltschmerz.GetElevation(x + (int) (chunk.x / Constants.VOXEL_SIZE), z + (int) (chunk.z / Constants.VOXEL_SIZE));
				for(int y = 0; y < Constants.CHUNK_SIZE1D; y++)
				{
					
					if(elevation > (int) (chunk.y / Constants.VOXEL_SIZE) + y){
						tempChunk[y + x * Constants.CHUNK_SIZE1D + z * Constants.CHUNK_SIZE2D] = materialID;
					}else
					{
						tempChunk[y + x * Constants.CHUNK_SIZE1D + z * Constants.CHUNK_SIZE2D] = 0;
					}
				}
			}
		}
	}

	private void MakeChunkBorders(Chunk chunk, int[] tempChunk)
	{
		int x, z;
		for(int s = 0; s < 6; s ++)
		{
			bool[] borders = chunk.Borders[s];
			switch(s)
			{
						//Front
				case 0:
					for(int i = 0; i < Constants.CHUNK_SIZE2D; i++)
					{
						x = i % Constants.CHUNK_SIZE1D;
						z = i / Constants.CHUNK_SIZE1D;
						borders[x + z * Constants.CHUNK_SIZE1D] = tempChunk[x + z * Constants.CHUNK_SIZE1D] != 0;
					}
				break;
						
						//Back
				case 1:
						for(int i = 0; i < Constants.CHUNK_SIZE2D; i++)
						{
							x = i % Constants.CHUNK_SIZE1D;
							z = i / Constants.CHUNK_SIZE1D;
							borders[x + z * Constants.CHUNK_SIZE1D] = tempChunk[x + z * Constants.CHUNK_SIZE1D + (Constants.CHUNK_SIZE3D - Constants.CHUNK_SIZE2D)] != 0;
						}
						break;

						//Right
						case 2:
						for(int i = 0; i < Constants.CHUNK_SIZE2D; i++)
						{
							x = i % Constants.CHUNK_SIZE1D;
							z = i / Constants.CHUNK_SIZE1D;
							borders[x + z * Constants.CHUNK_SIZE1D] = tempChunk[x + (Constants.CHUNK_SIZE2D - Constants.CHUNK_SIZE1D) + z * Constants.CHUNK_SIZE2D] != 0;
						}
						break;

						//Left
						case 3:
						for(int i = 0; i < Constants.CHUNK_SIZE2D; i++)
						{
							x = i % Constants.CHUNK_SIZE1D;
							z = i / Constants.CHUNK_SIZE1D;
							borders[x + z * Constants.CHUNK_SIZE1D] = tempChunk[x + z * Constants.CHUNK_SIZE2D] != 0;
						}
						break;

						//Top
						case 4:
						for(int i = 0; i < Constants.CHUNK_SIZE2D; i++)
						{
							x = i % Constants.CHUNK_SIZE1D;
							z = i / Constants.CHUNK_SIZE1D;
							borders[x + z * Constants.CHUNK_SIZE1D] = tempChunk[(Constants.CHUNK_SIZE1D - 1) + x * Constants.CHUNK_SIZE1D + z * Constants.CHUNK_SIZE2D] != 0;
						}
						break;

						//Bottom
						case 5:
						for(int i = 0; i < Constants.CHUNK_SIZE2D; i++)
						{
							x = i % Constants.CHUNK_SIZE1D;
							z = i / Constants.CHUNK_SIZE1D;
							borders[x + z * Constants.CHUNK_SIZE1D] = tempChunk[x * Constants.CHUNK_SIZE1D + z * Constants.CHUNK_SIZE2D] != 0;
						}
						break;
				}
		}
	}

	private void ParseToRLE(Chunk chunk, int[] tempChunk)
	{
		int prevId = -1;
		for(int i = 0; i < Constants.CHUNK_SIZE3D; i ++)
		{
			int id = tempChunk[i];

			if(id == prevId)
			{
			   Run run = chunk.Voxels[chunk.Voxels.Count - 1];
			   run.lenght ++;
			   chunk.Voxels[chunk.Voxels.Count - 1] = run;
			}
			else
			{
				prevId = id;
				Run run = new Run();
				run.lenght ++;
				run.value = id;
				chunk.Voxels.Add(run);
			}
		}


		if(chunk.Voxels.Count == 1)
		{
			chunk.IsSolid = true;
			if(chunk.Voxels[0].value == 0)
			{
				chunk.IsEmpty = true;
			}
			chunk.Materials = 1;
		}else
		{
			chunk.IsSurface = true;
		}

		chunk.IsFilled = true;
	}

	public int GetPrefillSize()
	{
		return fillPositions.Count;
	}
}
