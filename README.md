# Grass-Instancing
 
## Introduction 
Hello! Here is a grass system that I build purely for learning after watching Acerola's video about modern foliage. And also this project is my very first one after diving into learning Unity. For my future job opportunity, I will explain this project in detail :nerd_face::point_up:.

Check out Acerola's Youtube channel: [Modern Foliage Rendering](https://www.youtube.com/watch?v=jw00MbIJcrk)

## GPU Instancing
Normally, we create a game object if we want to render it on screen. Imagine manually placing 10.000 blade of grass in 100m field. Terrible, right? It is very costly and unscalable, so we have to find a new solution. And that is where GPU INSTANCING come into play. 

But what the difference between Game Object method and GPU Instancing method?
### Game Object 
1. **Existence in Both CPU and GPU:** The rendering object exists in both the GPU and CPU. Because of this, the objects can have physics applied, be manually transformed, and controlled.
2. **CPU Limitations:** The CPU is very intelligent but extremely slow. Having objects on the CPU causes extra overhead (a lot) to manage, apply changes to our grass, and send draw call instructions to the GPU for each blade. Keep in mind that sending draw calls is not free; in fact, it is highly inefficient.
3. **Performance Issues:** Because the GPU is very fast, the CPU crawling to send data may cause the GPU to stay idle for moments, which is not good. We want the GPU running efficiently. The real question is whether we need physics and other miscellaneous things in our grass and how to reduce draw calls and optimize GPU power.
    - **Physics:** If we apply physics to just 1,000 blades of grass with Rigidbody, the FPS will drop to a single digit.
    - **Wind:** We can easily simulate it by modifying the position of the grass's vertices over time in a shader.
    - **Batch Draw Calls:** Grass is identical, maybe except for orientation, scale, and color. There should be a way to send only one draw call and let the GPU render all the grass in one go with an acceptable frame rate.

### GPU Instancing
1. **Efficiency:** As mentioned above, this method meets all our needs. The GPU has the ability to perform parallel math computations extremely quickly.
2. **Limitations:** The downside is that objects no longer exist on the CPU side, which means no physics and no manual control. Fair enough!

## Compute Shader
We almost there! The only thing left is to calculate world position for every grass. It's gonna be easy, we can just toss million of calculations in CPU, right? Nuh uh, doing this way gonna be mad inefficient and your Unity will be dead mid way of the process. So what to do now :sob: ? Did I tell you that GPU is very good at doing math instruction, right ? Doing math, running on GPU... tadaaa! [Compute Shader](https://docs.unity3d.com/Manual/class-ComputeShader.html) is the one we need. It dispatches thread groups containing threads to do parallel computation for us. Basically, we calculate world pos and store them in StructureBuffer which share among both compute shader and normal shader. In grass shader, we get the data from the buffer and convert it to clip space pos, do a little color painting, random orientation and other things. Now we have grass in scene!

![Implementation](/Assets/Grass/Image/Grass.png)

## Optimization

### GPU Frustum Culling

One more downside of GPU Instancing, we can not get a free CPU's frustum culling like other game objects. We have to implement one ourself. We can achieve this with Vote, Scan and Compact method by using Compute Shader. \
1. **Vote:** General idea is checking each blade if it's inside camera view, and write it down into a ComputeBuffer named **Vote**. We can achieve it by using already stored world pos buffer and multiply the value with VP (view and perspective) matrix to get clip space pos. After that, executing perspective division to convert to NDC space and normalizing the range of NDC space to 0 - 1. Adding a little offset to each x, y, z to avoid flickering when blade is at near screen border. We can do distance cutoff in this block of code also.
2. **Scan:** a problem with Compute Shader is we can't just skip grass pos being outsite of view and jump to the next one. We have to modify the buffer, write down the positions of grass being valid consecutively and discard the rest. To achieve this, we have to implement Parallel Prefix Sum Algorithm[^1], which can be used to sort array, and run it on the previous **vote** buffer. First of all, this algorithm run by using binary tree, so the size of an buffer must be power of 2. Moreover, maximum thread in a thread group is 1024 thread; in other word, 1024 calculation. It basically means that we can not send buffer being more than 1024 elements. So we have to perform this algorithm on many thread groups. However, thread groups do not share value together, example: [0,0,1,0,0,1,1,1], we dispatch into 2 thread groups containing 4 threads each; thread group 1 run on half of the buffer [0,0,1,0] -> [0,0,0,1]; thread group 2 run on the rest [0,1,1,1] -> [0,0,1,2] (exclusive prefix sum). After storing them in a **Sum** buffer [0,0,0,1,0,0,1,2], you can clearly see that the latter group is not done yet. If doing prefix sum correctly, it's supposed to be[0,0,0,1] and [1,1,2,3]. To solve this this problem, we construct a new  buffer contains all the last element of each thread group (it's the sum of all elements in each thread group) and run the algorithm on it again, example:[0,0,0,1] -> 1 from first thread group,[0,0,1,2] -> 2 from second, construct a buffer named **GroupSum** -> [1,2]; run algorithm again [1,2] -> [1,3].
3. Compact: Final step, it basically adds value from **Group Sum** to **Sum Array**: [0,0,0,1,0,0,1,2] and [1,3] . The first half [0,0,0,1], it's right, no more modification but the second one [0,0,1,2], it needs to be added the sum of the first, which is [1] in group sum buffer. If there were a third group, added [3]. After all, we have this buffer [0,0,0,1,1,1,2,3]. What this buffer represents is it's a visible index buffer. Now look closely at the final **scan** buffer and **vote** buffer. First two 0 is not visible because of the 0 and 1 indices of **vote** buffer is 0 (invisible), but the third 0 in the **scan** buffer has 1 in **vote** buffer. It means the third position in **world pos** buffer is visible. We store the value in final buffer, culledGrassPos. We keep doing that till we are done. That's it!
\
![Exclusive and Inclusive Prefix Sum](https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcT4tM-AiRfC9bDW4zp033Uu1_BoHeBVIupQfQ&s)\
 *Exclusive and Inclusive Prefix Sum*
\
![How this algorithm is used to sort](https://developer.download.nvidia.com/books/gpugems3/39fig10.jpg)\
 *How this algorithm is used to sort*
\
![How to achieve this algorithm](https://developer.download.nvidia.com/books/gpugems3/39fig03.jpg)\
 *Up Sweep Phase*
\
![How to achieve this algorithm ](https://developer.download.nvidia.com/books/gpugems3/39fig04.jpg)\
 *Down Sweep Phase*
\
Maybe the example above is not comprehensible and easy to understand enough. I suggest you take your time to read the actual article to get the grip of it.

### Occlusion Culling

## Others

Added and tweaked a few more settings
![Swayed Grass](/Assets/Grass/Image/sway.gif)

## Wind System

HLSL does not have built-in perlin noise, so I implemented one by using power of CTRL C - CTRL V. After that, the noise is rendered into a render texture, let grass shader sample it. 
![Perlin Noise Wind System](/Assets/Grass/Image/wind.gif)
## Tasks

- :white_check_mark: Fog
- :white_check_mark: GPU Frustum Culling
- :white_check_mark: Chunk System
- :white_check_mark: LOD
- :white_check_mark: Better wind system
- :white_check_mark: Occlusion culling

## References
- [Parallel Prefix Sum](https://developer.nvidia.com/gpugems/gpugems3/part-vi-gpu-computing/chapter-39-parallel-prefix-sum-scan-cuda)
- [Acerola's project](https://github.com/GarrettGunnell/Grass)
- [Good tutorial on explaining Parallel Prefix Sum](https://www.youtube.com/watch?v=lavZl_wEbPE)
- [HLSL perlin noise](https://gist.github.com/fadookie/25adf86ae7e2753d717c#file-noisesimplex-cginc)
## Other Resources

- [Catlikecoding](https://catlikecoding.com/): He provides tons of good tutorial, like Rendering, GPU Instancing, Compute Shader....
- [Article about Perlin Noise](https://rtouti.github.io/graphics/perlin-noise-algorithm)
[^1]: https://developer.nvidia.com/gpugems/gpugems3/part-vi-gpu-computing/chapter-39-parallel-prefix-sum-scan-cuda
