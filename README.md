# Grass-Instancing
 
## Introduction 
Hello! Here is a grass system that I build purely for learning after watching Acerola's video about modern foliage. And also this project is my very first one after diving into learning Unity. For my future job opportunity, I will explain this project in detail :nerd_face::point_up:.

Check out Acerola's Youtube channel: ![Modern Foliage Rendering](https://www.youtube.com/watch?v=jw00MbIJcrk)

## GPU Instancing
Normally. we create a game object if we want it to render on screen, but this is very costly and unscalable. Imagine a 100-square-meter field with only 100 blades of grass. Terrible, right? So we have to find a new solution, and it is GPU INSTANCING. 

But what the difference between Game Object method and GPU Instancing method?
- Game Object method: CPU is a smart but slow guy and that's where a game object is created. It fills object with rendering components, then it sends all the necessary data to GPU to render. But there is a major problem with this way of rendering, that is we have to send data to GPU, which is largely unefficient, all over again for each object, even they are simillar. GPU is stupidly fast with them math, rendering instructions. It has to wait and remains idle until CPU send the next instruction. Because GPU is not performing at peak, this can lead to fewer FPS. This phenomenon is called Bottle Neck.
- GPU Instancing method: Instead of letting CPU intruct GPU every object, we can let it do only one. Million blades of grass are simillar, the basic idea that the necessary data only sent once and stayed in GPU, and GPU will do all the rendering jobs. We can achieve this through Graphics.DrawMeshInstancedIndirect function. One disadvantage of this method is that these blades of grass do not exist on CPU side, which means no physics, no control in Editor...

## Compute Shader
We almost there! The only thing left is to calculate world position for every grass. It's gonna be easy, we can just toss million of calculations in CPU, right? Nuh uh, doing this way gonna be mad inefficient and your Unity will be dead mid way of the process. So what to do now :sob: ? Did I tell you that GPU is very good at doing math instruction, right ? Doing math, running on CPU... tadaaa! [Compute Shader](https://docs.unity3d.com/Manual/class-ComputeShader.html) is the one we need :exploding_head:. It can dispatch thread groups containing threads to do parallel computation. 

Now we implement it. With my dumb shader knowledge, I implemented a basic sine wave wind system, make it colored and drooped. 

![Implementation](/Assets/Grass/Image/Grass.png)

## GPU Frustum Culling

Because of GPU Instancing, we can not get a free frustum culling like other game objects. We have to implement one ourself. We can achieve this with Vote, Scan and Compact method.
- Vote: each blade is checked if it's inside camera view, and write it down into a ComputeBuffer.
- Scan: a problem with Compute Shader is that it can not skip an element in position buffer array, so we can not just skip them grass being outsite of view. We have to modify the array, write down the positions of grass being valid consecutively and discard the rest. To achieve this, we have to implement Parallel Prefix Sum Algorithm[^1] and run it on the previous vote array buffer. But this algorithm only run in a single thread group which are 1024 threads - in other word, 1024 blades of grass. We have to perform this algorithm on many thread groups. However, thread groups do not share together, example: [0,0,1,0,0,1,1,1], we dispatch into 2 thread groups, 4 threads each group; thread group 1 run on half of the array [0,0,1,0] -> [0,0,1,1]; thread group 2 run on the rest [0,1,1,1] -> [0,1,2,3]. You can clearly see that 2 thread groups run independantly. To solve this this problem, we construct a new buffer array contains all the last element of each thread group (it's the sum of all elements in each thread group) and run the algorithm on it again, example: 1 from first thread group, 3 from second -> [1,3]; run algorithm [1,3] -> [1,4].
- Compact: Final step, IDK how to demonstrate this but this is an example: we got [0,0,1,1] and [0,1,2,3] from 2 thread groups and [1,4] from group sum array. The first half, it's right, no more modification but the second half, it need to be added the sum of first half, which stores in group sum array, to be done. Now, we have [0,0,1,1] and [1,2,3,4], I seperate 2 arrays for better visualization but they are actually one big array [0,0,1,1,1,2,3,4]. Now the final part, create a new ComputeBuffer array, culledPositionBuffer; check if the vote buffer's value at index i is equal to 1, if no, skip; if yes, take the value groupSumBufferArray[i], example : groupSumBufferArray[i] = 2. We write down into culledPositionBuffer that valute at index 2 = positionBuffer[i].

This topic took me 3 days just to have a solid grasp. Maybe the explaination abobe is not comprehensible and easy to understand enough. I suggest you take your time to read the actual article to get the grip of it.

## Others

Added and tweaked a few more settings
![Swayed Grass](/Assets/Grass/Image/sway.gif)

## Tasks

- :white_check_mark: ~~Fog~~
- :white_check_mark: ~~GPU Frustum Culling~~
- :white_check_mark: ~~Chunk System~~
- :white_check_mark: ~~LOD~~
- :x: Better wind system
- :x: Occlusion culling
- :x: Post processing

## References
- [Parallel Prefix Sum](https://developer.nvidia.com/gpugems/gpugems3/part-vi-gpu-computing/chapter-39-parallel-prefix-sum-scan-cuda)
- [Acerola's project](https://github.com/GarrettGunnell/Grass)
- [Good tutorial on explaining Parallel Prefix Sum](https://www.youtube.com/watch?v=lavZl_wEbPE)

## Other Resources

[Catlikecoding](https://catlikecoding.com/): He provides tons of good tutorial, like Rendering, GPU Instancing, Compute Shader....

[^1]: https://developer.nvidia.com/gpugems/gpugems3/part-vi-gpu-computing/chapter-39-parallel-prefix-sum-scan-cuda
